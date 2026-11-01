using System.Text.RegularExpressions;
using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Branding.Domain;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Branding.Application;

public sealed record BrandingInput(string BrandName, string? LogoUrl, string? PrimaryColor, string? AccentColor);
public sealed record BrandingView(Guid ApplicationId, string BrandName, string? LogoUrl, string? PrimaryColor, string? AccentColor);

/// <summary>
/// Per-tenant white-label branding (B6). Admins upsert/read a tenant's branding; the customer card app
/// reads the branding for the member's own application (member-token scope). Branding is not secret, so
/// reads run in system scope (so a child app can also be targeted by an ancestor admin).
/// </summary>
public sealed partial class BrandingService(
    IRepository<ApplicationBranding> brandingRepo,
    IRepository<Loyalty.Core.Modules.Applications.Domain.Application> applicationsRepo,
    ApplicationContext scope,
    IUnitOfWork uow,
    IClock clock)
{
    public async Task<BrandingView> PutAsync(Guid applicationId, BrandingInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.BrandName))
            throw new LoyaltyException(LoyaltyError.ValidationFailed("brandName is required."));
        var primary = CleanColor(input.PrimaryColor);
        var accent = CleanColor(input.AccentColor);

        scope.EnterSystemScope();
        try
        {
            if (!await applicationsRepo.Query().AnyAsync(a => a.Id == applicationId, ct))
                throw new LoyaltyException(LoyaltyError.NotFound("Application"));

            var existing = await brandingRepo.Query().FirstOrDefaultAsync(b => b.ApplicationId == applicationId, ct);
            if (existing is null)
                brandingRepo.Add(ApplicationBranding.Create(applicationId, input.BrandName.Trim(), Clean(input.LogoUrl), primary, accent, clock.UtcNow));
            else
                existing.Update(input.BrandName.Trim(), Clean(input.LogoUrl), primary, accent, clock.UtcNow);

            await uow.SaveChangesAsync(ct);
            return (await GetAsync(applicationId, ct))!;
        }
        finally { scope.LeaveSystemScope(); }
    }

    public async Task<BrandingView?> GetAsync(Guid applicationId, CancellationToken ct)
    {
        scope.EnterSystemScope();
        try
        {
            var b = await brandingRepo.Query().AsNoTracking().FirstOrDefaultAsync(x => x.ApplicationId == applicationId, ct);
            return b is null ? null : new BrandingView(b.ApplicationId, b.BrandName, b.LogoUrl, b.PrimaryColor, b.AccentColor);
        }
        finally { scope.LeaveSystemScope(); }
    }

    /// <summary>Remove a tenant's branding (reverts the customer app to the default theme). Returns
    /// false when there was none.</summary>
    public async Task<bool> DeleteAsync(Guid applicationId, CancellationToken ct)
    {
        scope.EnterSystemScope();
        try
        {
            var existing = await brandingRepo.Query().FirstOrDefaultAsync(b => b.ApplicationId == applicationId, ct);
            if (existing is null) return false;
            brandingRepo.Remove(existing);
            await uow.SaveChangesAsync(ct);
            return true;
        }
        finally { scope.LeaveSystemScope(); }
    }

    /// <summary>Branding for the current customer's application (member-token scope).</summary>
    public Task<BrandingView?> GetForCurrentAsync(CancellationToken ct) => GetAsync(scope.ApplicationId, ct);

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? CleanColor(string? s)
    {
        var v = Clean(s);
        if (v is null) return null;
        if (!HexColor().IsMatch(v)) throw new LoyaltyException(LoyaltyError.ValidationFailed("colors must be hex, e.g. #B18A43."));
        return v;
    }

    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex HexColor();
}
