using Microsoft.AspNetCore.Mvc;

namespace Loyalty.Api;

// Request DTOs (bilingual where user-facing) — shared across the API controllers.
public sealed record BootstrapRequest(string Name, string? NameAr);
public sealed record CreateApplicationRequest(string Name, string? NameAr, Guid? ParentId);
public sealed record SetStatusRequest(bool IsActive);
public sealed record CreateAccountRequest(string AccountName, DateTimeOffset? ExpiresAt);
public sealed record CreateMemberRequest(string ExternalCustomerId, DateOnly? BirthDate);
public sealed record SetBirthDateRequest(DateOnly? BirthDate);
public sealed record IssueTokenRequest(int? TtlDays);
public sealed record LoginRequest(string Username, string Password);
public sealed record SetRoleRequest(string Role);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record RedeemRequest(Guid RewardId);
public sealed record ConfirmByCodeRequest(string CouponCode);

/// <summary>
/// Shared base for the loyalty API controllers. Centralizes the small set of result shapes the
/// endpoints use so each action stays a one-liner, matching the previous minimal-API behavior
/// (status codes + JSON bodies) exactly.
/// </summary>
[ApiController]
public abstract class LoyaltyControllerBase : ControllerBase
{
    /// <summary>200 with the value, or 404 when it is null.</summary>
    protected IActionResult OkOrNotFound(object? value) => value is null ? NotFound() : Ok(value);

    /// <summary>204 when the operation succeeded, otherwise 404.</summary>
    protected IActionResult NoContentIf(bool ok) => ok ? NoContent() : (IActionResult)NotFound();

    /// <summary>201 Created with the value (no Location header), mirroring Results.Json(…, 201).</summary>
    protected IActionResult Created201(object value) => StatusCode(StatusCodes.Status201Created, value);

    /// <summary>A JSON error body { code, message } with the given status, matching the prior handlers.</summary>
    protected IActionResult Error(int status, string code, string message) => StatusCode(status, new { code, message });
}
