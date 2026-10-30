using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Gamification.Application;

public sealed record LeaderboardRow(int Rank, Guid MemberId, string ExternalCustomerId, decimal Value);

/// <summary>Ranks members by a value-profile metric (application-subtree scoped).</summary>
public sealed class LeaderboardService(LoyaltyDbContext db)
{
    public async Task<IReadOnlyList<LeaderboardRow>> TopAsync(string metric, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 100);
        var q = from p in db.ValueProfiles
                join m in db.Members on p.MemberId equals m.Id
                select new { p, m };

        var ordered = (metric?.ToLowerInvariant()) switch
        {
            "netspend" => q.OrderByDescending(x => x.p.NetSpendAmount).Select(x => new { x.m.Id, x.m.ExternalCustomerId, Value = x.p.NetSpendAmount }),
            "purchasecount" => q.OrderByDescending(x => x.p.PurchaseCount).Select(x => new { x.m.Id, x.m.ExternalCustomerId, Value = (decimal)x.p.PurchaseCount }),
            "streak" => q.OrderByDescending(x => x.p.CurrentStreak).Select(x => new { x.m.Id, x.m.ExternalCustomerId, Value = (decimal)x.p.CurrentStreak }),
            "logincount" => q.OrderByDescending(x => x.p.LoginCount).Select(x => new { x.m.Id, x.m.ExternalCustomerId, Value = (decimal)x.p.LoginCount }),
            "referralcount" => q.OrderByDescending(x => x.p.ReferralCount).Select(x => new { x.m.Id, x.m.ExternalCustomerId, Value = (decimal)x.p.ReferralCount }),
            _ => q.OrderByDescending(x => x.p.PointsEarned).Select(x => new { x.m.Id, x.m.ExternalCustomerId, Value = (decimal)x.p.PointsEarned }),
        };

        var rows = await ordered.Take(limit).ToListAsync(ct);
        return rows.Select((r, i) => new LeaderboardRow(i + 1, r.Id, r.ExternalCustomerId, r.Value)).ToList();
    }
}
