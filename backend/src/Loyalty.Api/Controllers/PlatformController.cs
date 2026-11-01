using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Accounts.Application;
using Loyalty.Core.Modules.Applications.Application;
using Loyalty.Core.Modules.Branding.Application;
using Loyalty.Core.Modules.Cvm.Application;
using Loyalty.Core.Modules.Cvm.Domain;
using Loyalty.Core.Modules.Engagement.Application;
using Loyalty.Core.Modules.Identity.Application;
using Loyalty.Core.Modules.Members.Application;
using Loyalty.Core.Modules.Settings.Application;
using Microsoft.AspNetCore.Mvc;

namespace Loyalty.Api.Controllers;

/// <summary>One-time tenant bootstrap, guarded by the X-Bootstrap-Token header.</summary>
[Tags("Bootstrap")]
public sealed class BootstrapController(IConfiguration cfg, ApplicationService apps, AccountService accounts, AdminIdentityService identity)
    : LoyaltyControllerBase
{
    [HttpPost("/api/v1/bootstrap")]
    public async Task<IActionResult> Bootstrap([FromBody] BootstrapRequest req, CancellationToken ct)
    {
        var token = cfg["Bootstrap:Token"];
        if (string.IsNullOrEmpty(token) || Request.Headers["X-Bootstrap-Token"].ToString() != token)
            return Error(401, "invalid_credential", "Invalid bootstrap token.");

        var (root, created) = await apps.BootstrapRootAsync(req.Name, req.NameAr, ct);
        AccountWithKey? key = created ? await accounts.CreateAsync(root.Id, "root-admin", null, ct) : null;
        // seed the first SuperAdmin against the root (no-op if any admin user already exists)
        var suUser = Environment.GetEnvironmentVariable("SUPERADMIN_USERNAME") ?? "superadmin";
        var suPass = Environment.GetEnvironmentVariable("SUPERADMIN_PASSWORD") ?? "ChangeMe!123";
        await identity.EnsureSuperAdminAsync(root.Id, suUser, suPass, ct);
        return StatusCode(created ? 201 : 200, new { application = root, created, rootAccount = key });
    }
}

/// <summary>Applications (tenant hierarchy) plus their loyalty / CVM / engagement settings.</summary>
[Tags("Applications")]
[Route("api/v1/applications")]
public sealed class ApplicationsController(
    ApplicationService apps,
    LoyaltySettingsService loyalty,
    CvmSettingsService cvm,
    EngagementSettingsService engagement,
    BrandingService branding,
    ApplicationContext scope) : LoyaltyControllerBase
{
    // current authenticated application (resolved from the API key) — used by the dashboard
    [HttpGet("/api/v1/me")]
    public IActionResult Me() => Ok(new { applicationId = scope.ApplicationId });

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateApplicationRequest req, CancellationToken ct) =>
        Created201(await apps.CreateAsync(req.Name, req.NameAr, req.ParentId, ct));

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await apps.ListVisibleAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => OkOrNotFound(await apps.GetAsync(id, ct));

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] SetStatusRequest req, CancellationToken ct)
    { await apps.SetStatusAsync(id, req.IsActive, ct); return NoContent(); }

    // ----- loyalty settings -----
    [HttpPut("{appId:guid}/loyalty-settings")]
    public async Task<IActionResult> PutLoyalty(Guid appId, [FromBody] PutLoyaltySettingsInput req, CancellationToken ct) =>
        Ok(await loyalty.PutAsync(appId, req, ct));

    [HttpGet("{appId:guid}/loyalty-settings")]
    public async Task<IActionResult> GetLoyalty(Guid appId, CancellationToken ct) =>
        OkOrNotFound(await loyalty.GetEffectiveAsync(appId, ct));

    [HttpDelete("{appId:guid}/loyalty-settings")]
    public async Task<IActionResult> DeleteLoyalty(Guid appId, CancellationToken ct) =>
        NoContentIf(await loyalty.DeleteAsync(appId, ct));

    // ----- CVM settings -----
    [HttpPut("{appId:guid}/cvm-settings")]
    public async Task<IActionResult> PutCvm(Guid appId, [FromBody] CvmFields req, CancellationToken ct) =>
        Ok(await cvm.PutAsync(appId, req, ct));

    [HttpGet("{appId:guid}/cvm-settings")]
    public async Task<IActionResult> GetCvm(Guid appId, CancellationToken ct) =>
        OkOrNotFound(await cvm.GetEffectiveAsync(appId, ct));

    [HttpDelete("{appId:guid}/cvm-settings")]
    public async Task<IActionResult> DeleteCvm(Guid appId, CancellationToken ct) =>
        NoContentIf(await cvm.DeleteAsync(appId, ct));

    // ----- engagement settings (P10) -----
    [HttpPut("{appId:guid}/engagement-settings")]
    public async Task<IActionResult> PutEngagement(Guid appId, [FromBody] EngagementSettingsInput req, CancellationToken ct) =>
        Ok(await engagement.UpsertAsync(appId, req, ct));

    [HttpGet("{appId:guid}/engagement-settings")]
    public async Task<IActionResult> GetEngagement(Guid appId, CancellationToken ct) =>
        Ok(await engagement.GetAsync(appId, ct));

    // ----- white-label branding (B6) -----
    [HttpPut("{appId:guid}/branding")]
    public async Task<IActionResult> PutBranding(Guid appId, [FromBody] BrandingInput req, CancellationToken ct) =>
        Ok(await branding.PutAsync(appId, req, ct));

    [HttpGet("{appId:guid}/branding")]
    public async Task<IActionResult> GetBranding(Guid appId, CancellationToken ct) =>
        OkOrNotFound(await branding.GetAsync(appId, ct));
}

/// <summary>API-key accounts: creation (per application), key rotation, status, revocation.</summary>
[Tags("Accounts")]
public sealed class AccountsController(AccountService accounts) : LoyaltyControllerBase
{
    [HttpPost("/api/v1/applications/{appId:guid}/accounts")]
    public async Task<IActionResult> Create(Guid appId, [FromBody] CreateAccountRequest req, CancellationToken ct) =>
        Created201(await accounts.CreateAsync(appId, req.AccountName, req.ExpiresAt, ct));

    [HttpGet("/api/v1/applications/{appId:guid}/accounts")]
    public async Task<IActionResult> List(Guid appId, CancellationToken ct) => Ok(await accounts.ListAsync(appId, ct));

    [HttpPatch("/api/v1/accounts/{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] SetStatusRequest req, CancellationToken ct)
    { await accounts.SetStatusAsync(id, req.IsActive, ct); return NoContent(); }

    [HttpPost("/api/v1/accounts/{id:guid}/rotate-key")]
    public async Task<IActionResult> RotateKey(Guid id, CancellationToken ct) => Ok(await accounts.RotateKeyAsync(id, ct));

    [HttpPost("/api/v1/accounts/{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    { await accounts.RevokeAsync(id, ct); return NoContent(); }
}

/// <summary>Browsable member list for the dashboard (search + limit).</summary>
[Tags("Customers")]
public sealed class CustomersController(MemberService members) : LoyaltyControllerBase
{
    [HttpGet("/api/v1/customers")]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] int? limit, CancellationToken ct) =>
        Ok(new { items = await members.ListAsync(search, limit ?? 100, ct) });
}
