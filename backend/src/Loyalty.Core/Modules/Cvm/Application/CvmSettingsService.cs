using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Cvm.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Cvm.Application;

public sealed record EffectiveCvmView(
    Guid ApplicationId, Guid SourceApplicationId, int SettingsVersion, bool IsInherited,
    string CvmStatus, int NewPeriodDays, int AtRiskAfterDays, int DormantAfterDays, int ChurnedAfterDays,
    decimal HighValueNetSpendThreshold, long FrequentPurchaseThreshold, int EvaluationWindowDays);

/// <summary>
/// Owns ApplicationCvmSettings with whole-row nearest-ancestor inheritance and independent
/// versioning (MVP spec §11.1). The resolver reads ancestor CVM config via the closure table
/// only — it never grants access to ancestor profiles/memberships — so it runs in system scope.
/// </summary>
public sealed class CvmSettingsService(LoyaltyDbContext db, ApplicationContext scope, IClock clock)
{
    public async Task<EffectiveCvmView> PutAsync(Guid applicationId, CvmFields fields, CancellationToken ct)
    {
        scope.EnterSystemScope();
        try
        {
            if (!await db.Applications.AnyAsync(a => a.Id == applicationId, ct))
                throw new LoyaltyException(LoyaltyError.NotFound("Application"));

            var existing = await db.CvmSettings.FirstOrDefaultAsync(s => s.ApplicationId == applicationId, ct);
            if (existing is null) db.CvmSettings.Add(ApplicationCvmSettings.Create(applicationId, fields, clock.UtcNow));
            else existing.Replace(fields, clock.UtcNow);

            await db.SaveChangesAsync(ct);
            return (await GetEffectiveAsync(applicationId, ct))!;
        }
        finally { scope.LeaveSystemScope(); }
    }

    public async Task<bool> DeleteAsync(Guid applicationId, CancellationToken ct)
    {
        scope.EnterSystemScope();
        try
        {
            var existing = await db.CvmSettings.FirstOrDefaultAsync(s => s.ApplicationId == applicationId, ct);
            if (existing is null) return false;
            db.CvmSettings.Remove(existing);
            await db.SaveChangesAsync(ct);
            return true;
        }
        finally { scope.LeaveSystemScope(); }
    }

    public async Task<EffectiveCvmView?> GetEffectiveAsync(Guid applicationId, CancellationToken ct)
    {
        var r = await ResolveEffectiveAsync(applicationId, ct);
        if (r is null) return null;
        return new EffectiveCvmView(applicationId, r.ApplicationId, r.Version, r.ApplicationId != applicationId,
            r.CvmStatus.ToString(), r.NewPeriodDays, r.AtRiskAfterDays, r.DormantAfterDays, r.ChurnedAfterDays,
            r.HighValueNetSpendThreshold, r.FrequentPurchaseThreshold, r.EvaluationWindowDays);
    }

    /// <summary>Nearest-ancestor whole-row resolution; null when nothing is configured.</summary>
    public async Task<ApplicationCvmSettings?> ResolveEffectiveAsync(Guid applicationId, CancellationToken ct)
    {
        var mustManage = !scope.IsSystem;
        if (mustManage) scope.EnterSystemScope();
        try
        {
            return await (
                from c in db.ApplicationClosures
                join s in db.CvmSettings on c.AncestorApplicationId equals s.ApplicationId
                where c.DescendantApplicationId == applicationId
                orderby c.Depth
                select s).FirstOrDefaultAsync(ct);
        }
        finally { if (mustManage) scope.LeaveSystemScope(); }
    }
}
