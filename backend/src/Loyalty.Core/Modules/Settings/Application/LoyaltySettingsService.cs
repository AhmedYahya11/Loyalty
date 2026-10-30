using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Settings.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Settings.Application;

public sealed record PutLoyaltySettingsInput(
    string PointsUnitCode, string BaseCurrency, decimal SpendAmountPerPoint,
    RoundingPolicy RoundingPolicy, LoyaltyStatus LoyaltyStatus,
    DateTimeOffset? LoyaltyStartsAtUtc, DateTimeOffset? LoyaltyEndsAtUtc, int? PointsExpiryDays = null);

public sealed record EffectiveSettingsView(
    Guid ApplicationId, Guid SourceApplicationId, int SettingsVersion, bool IsInherited,
    string PointsUnitCode, string BaseCurrency, decimal SpendAmountPerPoint,
    RoundingPolicy RoundingPolicy, LoyaltyStatus LoyaltyStatus,
    DateTimeOffset? LoyaltyStartsAtUtc, DateTimeOffset? LoyaltyEndsAtUtc, int PointsExpiryDays);

/// <summary>
/// Owns ApplicationLoyaltySettings with whole-row nearest-ancestor inheritance and
/// independent versioning (MVP spec §10). The nearest-ancestor resolution reads ancestor
/// settings via the closure table — a narrow capability that does NOT grant general access
/// to parent business data — so it runs in system scope.
/// </summary>
public sealed class LoyaltySettingsService(LoyaltyDbContext db, ApplicationContext scope, IClock clock)
{
    public async Task<EffectiveSettingsView> PutAsync(Guid applicationId, PutLoyaltySettingsInput input, CancellationToken ct)
    {
        scope.EnterSystemScope();
        try
        {
            if (string.IsNullOrWhiteSpace(input.PointsUnitCode)) throw new LoyaltyException(LoyaltyError.ValidationFailed("pointsUnitCode is required."));
            if (string.IsNullOrWhiteSpace(input.BaseCurrency)) throw new LoyaltyException(LoyaltyError.ValidationFailed("baseCurrency is required."));
            if (input.SpendAmountPerPoint <= 0) throw new LoyaltyException(LoyaltyError.ValidationFailed("spendAmountPerPoint must be greater than zero."));

            var appExists = await db.Applications.AnyAsync(a => a.Id == applicationId, ct);
            if (!appExists) throw new LoyaltyException(LoyaltyError.NotFound("Application"));

            var unit = input.PointsUnitCode.Trim().ToUpperInvariant();
            var existing = await db.LoyaltySettings.FirstOrDefaultAsync(s => s.ApplicationId == applicationId, ct);
            if (existing is null)
            {
                var created = ApplicationLoyaltySettings.Create(
                    applicationId, unit, input.BaseCurrency, input.SpendAmountPerPoint,
                    input.RoundingPolicy, input.LoyaltyStatus, input.LoyaltyStartsAtUtc, input.LoyaltyEndsAtUtc, clock.UtcNow, input.PointsExpiryDays ?? 0);
                db.LoyaltySettings.Add(created);
            }
            else
            {
                if (!string.Equals(existing.PointsUnitCode, unit, StringComparison.OrdinalIgnoreCase))
                {
                    var hasLedger = await db.LedgerTransactions.IgnoreQueryFilters().AnyAsync(t => t.ApplicationId == applicationId, ct);
                    if (hasLedger) throw new LoyaltyException(LoyaltyError.PointsUnitInUse());
                }
                existing.Replace(unit, input.BaseCurrency, input.SpendAmountPerPoint,
                    input.RoundingPolicy, input.LoyaltyStatus, input.LoyaltyStartsAtUtc, input.LoyaltyEndsAtUtc, clock.UtcNow, input.PointsExpiryDays ?? existing.PointsExpiryDays);
            }

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
            var existing = await db.LoyaltySettings.FirstOrDefaultAsync(s => s.ApplicationId == applicationId, ct);
            if (existing is null) return false;
            db.LoyaltySettings.Remove(existing);
            await db.SaveChangesAsync(ct);
            return true;
        }
        finally { scope.LeaveSystemScope(); }
    }

    public async Task<EffectiveSettingsView?> GetEffectiveAsync(Guid applicationId, CancellationToken ct)
    {
        var resolved = await ResolveEffectiveAsync(applicationId, ct);
        if (resolved is null) return null;
        return new EffectiveSettingsView(
            applicationId, resolved.ApplicationId, resolved.Version, resolved.ApplicationId != applicationId,
            resolved.PointsUnitCode, resolved.BaseCurrency, resolved.SpendAmountPerPoint,
            resolved.RoundingPolicy, resolved.LoyaltyStatus, resolved.LoyaltyStartsAtUtc, resolved.LoyaltyEndsAtUtc, resolved.PointsExpiryDays);
    }

    /// <summary>
    /// Nearest-ancestor whole-row resolution: if the application has its own row, use it;
    /// otherwise walk up the closure (ordered by depth ascending) and return the closest
    /// ancestor that has settings. Returns null when nothing is configured.
    /// </summary>
    public async Task<ApplicationLoyaltySettings?> ResolveEffectiveAsync(Guid applicationId, CancellationToken ct)
    {
        var mustManageScope = !scope.IsSystem;
        if (mustManageScope) scope.EnterSystemScope();
        try
        {
            var chain = await (
                from c in db.ApplicationClosures
                join s in db.LoyaltySettings on c.AncestorApplicationId equals s.ApplicationId
                where c.DescendantApplicationId == applicationId
                orderby c.Depth
                select s).ToListAsync(ct);
            return chain.FirstOrDefault();
        }
        finally { if (mustManageScope) scope.LeaveSystemScope(); }
    }
}
