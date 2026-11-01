using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Cvm.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Cvm.Application;

public sealed record RfmPersonaCount(string Persona, int Count);
public sealed record RfmSummary(int Members, int Evaluated, double AvgRecency, double AvgFrequency, double AvgMonetary, IReadOnlyList<RfmPersonaCount> Personas);

/// <summary>
/// Recompute per-member RFM scores + personas (P12). Scores are relative quintiles within each
/// application's own member population, so the segmentation adapts to every tenant. Runs in system
/// scope across all applications (background job / admin trigger) or for the current scope (summary).
/// Mirrors the lifecycle sweep.
/// </summary>
public sealed class RfmService(IRepository<Loyalty.Core.Modules.Cvm.Domain.MemberValueProfile> valueProfilesRepo, IUnitOfWork uow, ApplicationContext scope, IClock clock)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        scope.EnterSystemScope();
        try
        {
            var appIds = await valueProfilesRepo.Query().Select(p => p.ApplicationId).Distinct().ToListAsync(ct);
            var total = 0;
            foreach (var appId in appIds)
                total += await ComputeAsync(appId, ct);
            return total;
        }
        finally { scope.LeaveSystemScope(); }
    }

    private async Task<int> ComputeAsync(Guid appId, CancellationToken ct)
    {
        var profiles = await valueProfilesRepo.Query().Where(p => p.ApplicationId == appId).ToListAsync(ct);
        if (profiles.Count == 0) return 0;
        var now = clock.UtcNow;

        // recency value: more recent → larger (we negate days-since so the quintile ranks correctly)
        double RecencyValue(Domain.MemberValueProfile p)
        {
            var last = p.LastPurchaseAt ?? p.LastActivityAt;
            if (last is null) return -3650; // no activity → effectively the oldest
            return -(now - last.Value).TotalDays;
        }

        var recencyAsc = profiles.Select(RecencyValue).OrderBy(x => x).ToList();
        var freqAsc = profiles.Select(p => (double)p.PurchaseCount).OrderBy(x => x).ToList();
        var monAsc = profiles.Select(p => (double)p.NetSpendAmount).OrderBy(x => x).ToList();

        foreach (var p in profiles)
        {
            var r = Rfm.Quintile(RecencyValue(p), recencyAsc);
            var f = Rfm.Quintile(p.PurchaseCount, freqAsc);
            var m = Rfm.Quintile((double)p.NetSpendAmount, monAsc);
            p.SetRfm(r, f, m, Rfm.Classify(r, f, m), now);
        }
        await uow.SaveChangesAsync(ct);
        return profiles.Count;
    }

    /// <summary>Persona distribution + average scores for the current (admin) application scope.</summary>
    public async Task<RfmSummary> SummaryAsync(CancellationToken ct)
    {
        var profiles = await valueProfilesRepo.Query().ToListAsync(ct);
        var evaluated = profiles.Where(p => p.RfmEvaluatedAt != null).ToList();
        var byPersona = evaluated
            .GroupBy(p => p.RfmPersona ?? "Lost")
            .Select(g => new RfmPersonaCount(g.Key, g.Count()))
            .OrderByDescending(x => x.Count).ToList();
        return new RfmSummary(
            profiles.Count, evaluated.Count,
            evaluated.Count == 0 ? 0 : evaluated.Average(p => p.RfmRecency),
            evaluated.Count == 0 ? 0 : evaluated.Average(p => p.RfmFrequency),
            evaluated.Count == 0 ? 0 : evaluated.Average(p => p.RfmMonetary),
            byPersona);
    }
}
