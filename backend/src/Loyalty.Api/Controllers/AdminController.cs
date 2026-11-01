using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Cvm.Application;
using Loyalty.Core.Modules.Engagement.Application;
using Loyalty.Core.Modules.Identity.Application;
using Loyalty.Core.Modules.Wallets.Application;
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

    [HttpGet("me")]
    public IActionResult Me() =>
        me.IsEstablished
            ? Ok(new { username = me.Username, role = me.Role.ToString(), applicationId = me.ApplicationId, canManageUsers = me.CanManageUsers })
            : Error(401, "invalid_credential", "Not logged in as an admin.");

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req, CancellationToken ct)
    {
        if (!me.IsEstablished) return Error(401, "invalid_credential", "Log in as an admin.");
        var h = Request.Headers.Authorization.ToString();
        var current = h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? h["Bearer ".Length..].Trim() : h;
        return await identity.ChangePasswordAsync(me.UserId, req.CurrentPassword, req.NewPassword, current, ct)
            ? NoContent()
            : Error(400, "invalid_credential", "Current password is incorrect.");
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var h = Request.Headers.Authorization.ToString();
        await identity.RevokeAsync(h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? h["Bearer ".Length..].Trim() : h, ct);
        return NoContent();
    }

    [HttpGet("roles")]
    public IActionResult Roles() =>
        me.IsEstablished ? Ok(new[]
        {
            new { role = "Viewer", description = "Read-only access to all data." },
            new { role = "Manager", description = "Manage loyalty config (campaigns, rewards, tiers, …)." },
            new { role = "Admin", description = "Everything in the tenant, including managing users." },
            new { role = "SuperAdmin", description = "Full access across all tenants, including admins." },
        }) : Error(401, "invalid_credential", "Not logged in.");

    [HttpGet("users")]
    public async Task<IActionResult> ListUsers(CancellationToken ct) =>
        me.CanManageUsers ? Ok(await identity.ListUsersAsync(me, ct)) : Error(403, "forbidden", "Admins only.");

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateAdminUserInput req, CancellationToken ct) =>
        me.CanManageUsers ? Created201(await identity.CreateUserAsync(me, req, ct)) : Error(403, "forbidden", "Admins only.");

    [HttpPut("users/{id:guid}/role")]
    public async Task<IActionResult> SetRole(Guid id, [FromBody] SetRoleRequest req, CancellationToken ct) =>
        !me.CanManageUsers ? Error(403, "forbidden", "Admins only.")
            : OkOrNotFound(await identity.SetRoleAsync(me, id, req.Role, ct));

    [HttpPost("users/{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) =>
        !me.CanManageUsers ? Error(403, "forbidden", "Admins only.")
            : NoContentIf(await identity.DeactivateAsync(me, id, ct));

    // ----- on-demand maintenance jobs -----
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
