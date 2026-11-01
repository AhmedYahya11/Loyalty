using Microsoft.AspNetCore.Identity;

namespace Loyalty.Core.Modules.Identity.Domain;

/// <summary>Dashboard roles (P17). Higher roles include the capabilities of lower ones.</summary>
public enum AdminRole
{
    Viewer = 0,   // read-only
    Manager = 1,  // manage loyalty config (campaigns, rewards, …), not users
    Admin = 2,    // everything in the tenant incl. managing users
    SuperAdmin = 3, // everything across all tenants incl. managing admins
}

/// <summary>
/// A dashboard operator (P17), now backed by ASP.NET Core Identity. Identity owns the username,
/// normalized lookups and password hashing (via <see cref="UserManager{TUser}"/>); the operator's
/// role is an Identity role. We extend the base user with the multi-tenant <see cref="ApplicationId"/>
/// (a SuperAdmin on the root sees the whole tree) and an active flag. Identity metadata is NOT
/// application-scoped (resolved during anonymous login, like <c>Account</c>).
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public Guid ApplicationId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A hashed, expiring dashboard login session (P17). Only the SHA-256 hash is stored.
/// This is the server-side bearer-token ("as_") scheme the auth middleware resolves.</summary>
public sealed class AdminSession
{
    public Guid Id { get; private set; }
    public Guid AdminUserId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    private AdminSession() { }

    public AdminSession(Guid adminUserId, string tokenHash, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        Id = Guid.NewGuid(); AdminUserId = adminUserId; TokenHash = tokenHash; ExpiresAt = expiresAt; CreatedAt = now;
    }

    public bool IsValid(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
    public void Revoke(DateTimeOffset now) => RevokedAt = now;
}
