using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Campaigns.Domain;
using Loyalty.Core.Modules.Cvm.Domain;
using Loyalty.Core.Modules.Wallets.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Analytics;

/// <summary>Aggregate read model for the dashboard overview, scoped to the authenticated subtree.</summary>
public sealed class AnalyticsService(IRepository<Loyalty.Core.Modules.Members.Domain.Member> membersRepo, IRepository<Loyalty.Core.Modules.Campaigns.Domain.Campaign> campaignsRepo, IRepository<Loyalty.Core.Modules.Cvm.Domain.SegmentDefinition> segmentsRepo, IRepository<Loyalty.Core.Modules.Wallets.Domain.Wallet> walletsRepo, IRepository<Loyalty.Core.Modules.Cvm.Domain.MemberValueProfile> valueProfilesRepo, IRepository<Loyalty.Core.Modules.Wallets.Domain.PointsLedgerTransaction> ledgerTransactionsRepo, IRepository<Loyalty.Core.Modules.Tiers.Domain.Tier> tiersRepo)
{
    public async Task<object> OverviewAsync(CancellationToken ct)
    {
        var members = await membersRepo.Query().CountAsync(ct);
        var activeCampaigns = await campaignsRepo.Query().CountAsync(c => c.Status == CampaignStatus.Active, ct);
        var activeSegments = await segmentsRepo.Query().CountAsync(s => s.Status == SegmentStatus.Active, ct);
        var activeBalance = await walletsRepo.Query().SumAsync(w => (long?)w.ActiveBalance, ct) ?? 0;
        var pointsIssued = await valueProfilesRepo.Query().SumAsync(p => (long?)p.PointsEarned, ct) ?? 0;

        var pointsReversed = await ledgerTransactionsRepo.Query()
            .Where(t => t.TransactionType == TransactionType.Reverse).SumAsync(t => (long?)t.TotalCredit, ct) ?? 0;

        var lifecycleRaw = await valueProfilesRepo.Query()
            .GroupBy(p => p.LifecycleStage)
            .Select(g => new { Stage = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var lifecycle = Enum.GetValues<LifecycleStage>()
            .ToDictionary(s => s.ToString(), s => lifecycleRaw.FirstOrDefault(x => x.Stage == s)?.Count ?? 0);

        // tier distribution (members per tier name; untiered members grouped under "None")
        var tierRaw = await (
            from p in valueProfilesRepo.Query()
            join t in tiersRepo.Query() on p.CurrentTierId equals t.Id into tj
            from t in tj.DefaultIfEmpty()
            group p by t != null ? t.Name : "None" into g
            select new { Tier = g.Key, Count = g.Count() }).ToListAsync(ct);
        var tiers = tierRaw.ToDictionary(x => x.Tier, x => x.Count);

        return new
        {
            members,
            activeCampaigns,
            activeSegments,
            pointsIssued,
            pointsReversed,
            activeBalance,
            lifecycle,
            tiers,
        };
    }
}
