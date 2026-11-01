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
public sealed class CvmSettingsService(IRepository<Loyalty.Core.Modules.Applications.Domain.Application> applicationsRepo, IRepository<Loyalty.Core.Modules.Cvm.Domain.ApplicationCvmSettings> cvmSettingsRepo, IRepository<Loyalty.Core.Modules.Applications.Domain.ApplicationClosure> applicationClosuresRepo, IUnitOfWork uow, ApplicationContext scope, IClock clock)
{
    public async Task<EffectiveCvmView> PutAsync(Guid applicationId, CvmFields fields, CancellationToken ct)
    {
        scope.EnterSystemScope();
        try
        {
            if (!await applicationsRepo.Query().AnyAsync(a => a.Id == applicationId, ct))
                throw new LoyaltyException(LoyaltyError.NotFound("Application"));

            var existing = await cvmSettingsRepo.Query().FirstOrDefaultAsync(s => s.ApplicationId == applicationId, ct);
            if (existing is null) cvmSettingsRepo.Add(ApplicationCvmSettings.Create(applicationId, fields, clock.UtcNow));
            else existing.Replace(fields, clock.UtcNow);

            await uow.SaveChangesAsync(ct);
            return (await GetEffectiveAsync(applicationId, ct))!;
        }
        finally { scope.LeaveSystemScope(); }
    }

    public async Task<bool> DeleteAsync(Guid applicationId, CancellationToken ct)
    {
        scope.EnterSystemScope();
        try
        {
            var existing = await cvmSettingsRepo.Query().FirstOrDefaultAsync(s => s.ApplicationId == applicationId, ct);
            if (existing is null) return false;
            cvmSettingsRepo.Remove(existing);
            await uow.SaveChangesAsync(ct);
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
                from c in applicationClosuresRepo.Query()
                join s in cvmSettingsRepo.Query() on c.AncestorApplicationId equals s.ApplicationId
                where c.DescendantApplicationId == applicationId
                orderby c.Depth
                select s).FirstOrDefaultAsync(ct);
        }
        finally { if (mustManage) scope.LeaveSystemScope(); }
    }
}
