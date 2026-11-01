using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Rewards.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Analytics;

/// <summary>
/// Promotion-performance read model for the dashboard (P14), scoped to the authenticated subtree:
/// per-campaign usage + budget, per-reward redemption funnel + rate, stamp-card completions, overall
/// AOV, and the outstanding points/credit liability.
/// </summary>
public sealed class PromotionAnalyticsService(IRepository<Loyalty.Core.Modules.Campaigns.Domain.CampaignUsage> campaignUsagesRepo, IRepository<Loyalty.Core.Modules.Campaigns.Domain.Campaign> campaignsRepo, IRepository<Loyalty.Core.Modules.Rewards.Domain.Redemption> redemptionsRepo, IRepository<Loyalty.Core.Modules.Rewards.Domain.Reward> rewardsRepo, IRepository<Loyalty.Core.Modules.Engagement.Domain.MemberStampCard> memberStampCardsRepo, IRepository<Loyalty.Core.Modules.Engagement.Domain.StampCard> stampCardsRepo, IRepository<Loyalty.Core.Modules.Cvm.Domain.MemberValueProfile> valueProfilesRepo, IRepository<Loyalty.Core.Modules.Wallets.Domain.Wallet> walletsRepo)
{
    public async Task<object> SummaryAsync(CancellationToken ct)
    {
        // --- campaigns: executions, members, points issued, budget ---
        var usage = await campaignUsagesRepo.Query()
            .GroupBy(u => u.CampaignId)
            .Select(g => new { CampaignId = g.Key, Executions = g.Sum(x => x.ExecutionCount), Members = g.Select(x => x.MemberId).Distinct().Count(), Points = g.Sum(x => x.IssuedPoints) })
            .ToListAsync(ct);
        var usageById = usage.ToDictionary(u => u.CampaignId);
        var campaigns = await campaignsRepo.Query()
            .Select(c => new { c.Id, c.Name, c.NameAr, Status = c.Status.ToString(), c.GlobalBudgetInitial, c.GlobalBudgetRemaining })
            .ToListAsync(ct);
        var campaignReport = campaigns.Select(c => new
        {
            c.Id, c.Name, c.NameAr, c.Status,
            executions = usageById.TryGetValue(c.Id, out var u) ? u.Executions : 0,
            members = usageById.TryGetValue(c.Id, out var u2) ? u2.Members : 0,
            pointsIssued = usageById.TryGetValue(c.Id, out var u3) ? u3.Points : 0,
            budgetInitial = c.GlobalBudgetInitial,
            budgetRemaining = c.GlobalBudgetRemaining,
        }).OrderByDescending(c => c.pointsIssued).ToList();

        // --- rewards: redemption funnel + rate ---
        var reds = await redemptionsRepo.Query()
            .GroupBy(r => new { r.RewardId, r.Status })
            .Select(g => new { g.Key.RewardId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);
        var rewards = await rewardsRepo.Query().Select(r => new { r.Id, r.Name, r.NameAr, r.PointsCost }).ToListAsync(ct);
        var rewardReport = rewards.Select(r =>
        {
            int Count(RedemptionStatus s) => reds.Where(x => x.RewardId == r.Id && x.Status == s).Sum(x => x.Count);
            var reserved = Count(RedemptionStatus.Reserved);
            var confirmed = Count(RedemptionStatus.Confirmed);
            var cancelled = Count(RedemptionStatus.Cancelled);
            var total = reserved + confirmed + cancelled;
            return new { r.Id, r.Name, r.NameAr, r.PointsCost, reserved, confirmed, cancelled, redemptionRate = total == 0 ? 0d : Math.Round((double)confirmed / total, 3) };
        }).OrderByDescending(r => r.confirmed).ToList();

        // --- stamp cards: completions + participants ---
        var stampAgg = await memberStampCardsRepo.Query()
            .GroupBy(m => m.StampCardId)
            .Select(g => new { StampCardId = g.Key, Completions = g.Sum(x => x.CompletedCount), Participants = g.Count() })
            .ToListAsync(ct);
        var stampAggById = stampAgg.ToDictionary(x => x.StampCardId);
        var stampCards = await stampCardsRepo.Query().Select(c => new { c.Id, c.Name, c.NameAr, c.RewardPoints }).ToListAsync(ct);
        var stampReport = stampCards.Select(c => new
        {
            c.Id, c.Name, c.NameAr, c.RewardPoints,
            completions = stampAggById.TryGetValue(c.Id, out var s) ? s.Completions : 0,
            participants = stampAggById.TryGetValue(c.Id, out var s2) ? s2.Participants : 0,
        }).ToList();

        // --- value + liabilities ---
        var totalPurchases = await valueProfilesRepo.Query().SumAsync(p => (long?)p.PurchaseCount, ct) ?? 0;
        var totalNetSpend = await valueProfilesRepo.Query().SumAsync(p => (decimal?)p.NetSpendAmount, ct) ?? 0m;
        var outstandingActive = await walletsRepo.Query().SumAsync(w => (long?)w.ActiveBalance, ct) ?? 0;
        var outstandingReserved = await walletsRepo.Query().SumAsync(w => (long?)w.ReservedBalance, ct) ?? 0;

        return new
        {
            campaigns = campaignReport,
            rewards = rewardReport,
            stampCards = stampReport,
            averageOrderValue = totalPurchases == 0 ? 0m : Math.Round(totalNetSpend / totalPurchases, 2),
            totalPurchases,
            totalNetSpend,
            liabilities = new { outstandingPoints = outstandingActive, reservedPoints = outstandingReserved },
        };
    }
}
