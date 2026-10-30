using Loyalty.Core.Modules.Campaigns.Domain;
using Loyalty.Core.Modules.Cvm.Domain;
using Loyalty.Core.Modules.Wallets.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Analytics;

/// <summary>Aggregate read model for the dashboard overview, scoped to the authenticated subtree.</summary>
public sealed class AnalyticsService(LoyaltyDbContext db)
{
    public async Task<object> OverviewAsync(CancellationToken ct)
    {
        var members = await db.Members.CountAsync(ct);
        var activeCampaigns = await db.Campaigns.CountAsync(c => c.Status == CampaignStatus.Active, ct);
        var activeSegments = await db.Segments.CountAsync(s => s.Status == SegmentStatus.Active, ct);
        var activeBalance = await db.Wallets.SumAsync(w => (long?)w.ActiveBalance, ct) ?? 0;
        var pointsIssued = await db.ValueProfiles.SumAsync(p => (long?)p.PointsEarned, ct) ?? 0;

        var pointsReversed = await db.LedgerTransactions
            .Where(t => t.TransactionType == TransactionType.Reverse).SumAsync(t => (long?)t.TotalCredit, ct) ?? 0;

        var lifecycleRaw = await db.ValueProfiles
            .GroupBy(p => p.LifecycleStage)
            .Select(g => new { Stage = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var lifecycle = Enum.GetValues<LifecycleStage>()
            .ToDictionary(s => s.ToString(), s => lifecycleRaw.FirstOrDefault(x => x.Stage == s)?.Count ?? 0);

        return new
        {
            members,
            activeCampaigns,
            activeSegments,
            pointsIssued,
            pointsReversed,
            activeBalance,
            lifecycle,
        };
    }
}
