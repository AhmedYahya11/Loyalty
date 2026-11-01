using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Cvm.Application;
using Loyalty.Core.Modules.Engagement.Application;
using Loyalty.Core.Modules.Identity.Application;
using Loyalty.Core.Modules.Wallets.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Loyalty.Api.Controllers;

/// <summary>Dashboard operator identity + RBAC (P17) and on-demand admin maintenance jobs.</summary>
[Tags("Admin")]
[Route("api/v1/admin")]
public sealed class AdminController(
    AdminIdentityService identity,
    AdminContext me,
    LifecycleSweepService lifecycle,
    PointsExpiryService expiry,
    RfmService rfm,
    BirthdaySweepService birthday) : LoyaltyControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct) =>
        await identity.LoginAsync(req.Username, req.Password, ct) is { } r
            ? Ok(new { token = r.Token, expiresAt = r.ExpiresAt, username = r.Username, role = r.Role, applicationId = r.ApplicationId })
            : Error(401, "invalid_credential", "Invalid username or password.");

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me() =>
        Ok(new { username = me.Username, role = me.Role.ToString(), applicationId = me.ApplicationId, canManageUsers = me.CanManageUsers });

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req, CancellationToken ct) =>
        await identity.ChangePasswordAsync(me.UserId, req.CurrentPassword, req.NewPassword, me.SessionId, ct)
            ? NoContent()
            : Error(400, "invalid_credential", "Current password is incorrect.");

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await identity.RevokeSessionAsync(me.SessionId, ct);
        return NoContent();
    }

    [Authorize]
    [HttpGet("roles")]
    public IActionResult Roles() =>
        Ok(new[]
        {
            new { role = "Viewer", description = "Read-only access to all data." },
            new { role = "Manager", description = "Manage loyalty config (campaigns, rewards, tiers, …)." },
            new { role = "Admin", description = "Everything in the tenant, including managing users." },
            new { role = "SuperAdmin", description = "Full access across all tenants, including admins." },
        });

    [Authorize(Policy = "ManageUsers")]
    [HttpGet("users")]
    public async Task<IActionResult> ListUsers(CancellationToken ct) => Ok(await identity.ListUsersAsync(me, ct));

    [Authorize(Policy = "ManageUsers")]
    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateAdminUserInput req, CancellationToken ct) =>
        Created201(await identity.CreateUserAsync(me, req, ct));

    [Authorize(Policy = "ManageUsers")]
    [HttpPut("users/{id:guid}/role")]
    public async Task<IActionResult> SetRole(Guid id, [FromBody] SetRoleRequest req, CancellationToken ct) =>
        OkOrNotFound(await identity.SetRoleAsync(me, id, req.Role, ct));

    [Authorize(Policy = "ManageUsers")]
    [HttpPost("users/{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) =>
        NoContentIf(await identity.DeactivateAsync(me, id, ct));

    // ----- on-demand maintenance jobs: operational endpoints, callable with an API key or an
    //       admin token (scope + Viewer read-only enforced by the auth middleware) — not [Authorize]. -----
    [HttpPost("lifecycle-sweep")]
    public async Task<IActionResult> LifecycleSweep(CancellationToken ct) =>
        Ok(new { transitions = await lifecycle.RunAsync(ct) });

    [HttpPost("expire-points")]
    public async Task<IActionResult> ExpirePoints([FromQuery] string? asOf, CancellationToken ct) =>
        Ok(new { expired = asOf is not null && DateTimeOffset.TryParse(asOf, out var d) ? await expiry.RunForAsync(d, ct) : await expiry.RunAsync(ct) });

    [HttpPost("rfm-recompute")]
    public async Task<IActionResult> RfmRecompute(CancellationToken ct) =>
        Ok(new { scored = await rfm.RunAsync(ct) });

    [HttpPost("birthday-sweep")]
    public async Task<IActionResult> BirthdaySweep([FromQuery] string? asOf, CancellationToken ct) =>
        Ok(new { rewarded = asOf is not null && DateTimeOffset.TryParse(asOf, out var d) ? await birthday.RunForAsync(d, ct) : await birthday.RunAsync(ct) });
}
