using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Campaigns.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Campaigns.Application;

/// <summary>
/// The impure side of campaign processing (MVP spec §12, diagram 4): member-limit filtering,
/// atomic global-budget reservation, and usage recording. Operates on tracked entities inside
/// the handler's transaction; the caller commits.
/// </summary>
public sealed class CampaignEngine(LoyaltyDbContext db, IClock clock)
{
    /// <summary>Drop campaigns whose member limit for the event's period is already reached.</summary>
    public async Task<List<Campaign>> FilterByMemberLimitsAsync(
        IReadOnlyList<Campaign> eligible, Guid memberId, DateTimeOffset occurredAt, CancellationToken ct)
    {
        var surviving = new List<Campaign>();
        foreach (var c in eligible)
        {
            var ok = true;
            foreach (var limit in c.MemberLimits)
            {
                var key = limit.PeriodKey(occurredAt);
                var usage = await db.CampaignUsages.FirstOrDefaultAsync(
                    u => u.CampaignId == c.Id && u.MemberId == memberId && u.PeriodKey == key, ct);
                if (usage is not null && usage.ExecutionCount >= limit.MaximumExecutions) { ok = false; break; }
            }
            if (ok) surviving.Add(c);
        }
        return surviving;
    }

    /// <summary>
    /// Atomically reserve budget: UPDATE ... SET remaining = remaining - n WHERE remaining >= n.
    /// Returns true when reserved (or the campaign has no budget). Runs in the current transaction.
    /// </summary>
    public async Task<bool> TryReserveBudgetAsync(Campaign campaign, long points, CancellationToken ct)
    {
        if (campaign.GlobalBudgetInitial is null || points <= 0) return true;
        var affected = await db.Campaigns
            .Where(c => c.Id == campaign.Id && c.GlobalBudgetRemaining >= points)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.GlobalBudgetRemaining, c => c.GlobalBudgetRemaining - points), ct);
        return affected == 1;
    }

    /// <summary>Record one execution for each of the campaign's limit periods (or AllTime).</summary>
    public async Task RecordUsageAsync(Campaign campaign, Guid memberId, DateTimeOffset occurredAt, long issuedPoints, CancellationToken ct)
    {
        var keys = campaign.MemberLimits.Count > 0
            ? campaign.MemberLimits.Select(l => l.PeriodKey(occurredAt)).Distinct()
            : ["all"];

        foreach (var key in keys)
        {
            var usage = await db.CampaignUsages.FirstOrDefaultAsync(
                u => u.CampaignId == campaign.Id && u.MemberId == memberId && u.PeriodKey == key, ct);
            if (usage is null)
                db.CampaignUsages.Add(CampaignUsage.Start(campaign.ApplicationId, campaign.Id, memberId, key, issuedPoints, clock.UtcNow));
            else
                usage.Record(issuedPoints, clock.UtcNow);
        }
    }
}
