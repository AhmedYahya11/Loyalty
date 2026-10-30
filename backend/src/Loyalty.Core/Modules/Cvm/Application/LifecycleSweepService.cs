using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Cvm.Application;

/// <summary>
/// The daily batched lifecycle evaluation for inactivity transitions (MVP spec §11.3's
/// EvaluateLifecycleStagesJob). Per-event evaluation handles activity-driven transitions; this
/// sweep moves members to AtRisk/Dormant/Churned when they simply stop interacting. Runs in
/// system scope over one captured timestamp per application.
///
/// NOTE (Phase 2): sweeps applications that have their own active CVM settings. Descendants
/// that only inherit settings are covered when they receive events; a full inherited-sweep is
/// a later enhancement (docs/DEVIATIONS.md).
/// </summary>
public sealed class LifecycleSweepService(LoyaltyDbContext db, ApplicationContext scope, CvmService cvm, IClock clock)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        scope.EnterSystemScope();
        try
        {
            var appIds = await db.CvmSettings
                .Where(s => s.CvmStatus == Domain.CvmStatus.Active)
                .Select(s => s.ApplicationId).ToListAsync(ct);

            var transitions = 0;
            foreach (var appId in appIds)
            {
                var cvmSettings = await db.CvmSettings.FirstAsync(s => s.ApplicationId == appId, ct);
                var profiles = await db.ValueProfiles.Where(p => p.ApplicationId == appId).ToListAsync(ct);
                var now = clock.UtcNow;
                foreach (var p in profiles)
                {
                    if (cvm.EvaluateLifecycle(p, cvmSettings, now)) transitions++;
                    await cvm.EvaluateSegmentsAsync(p, now, ct);
                }
                await db.SaveChangesAsync(ct);
            }
            return transitions;
        }
        finally { scope.LeaveSystemScope(); }
    }
}
