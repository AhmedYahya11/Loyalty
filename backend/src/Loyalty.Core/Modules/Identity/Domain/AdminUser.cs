using System.Security.Cryptography;

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
/// A dashboard operator (P17). Authenticates with username + password (PBKDF2) and is scoped to an
/// application; a SuperAdmin on the root application sees the whole tree. Identity metadata — NOT
/// application-scoped (resolved during anonymous login, like <c>Account</c>).
/// </summary>
public sealed class AdminUser
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public string Username { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public AdminRole Role { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private AdminUser() { }

    public static AdminUser Create(Guid applicationId, string username, string passwordHash, AdminRole role, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ApplicationId = applicationId,
        Username = username.Trim().ToLowerInvariant(),
        PasswordHash = passwordHash,
        Role = role,
        IsActive = true,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void SetRole(AdminRole role, DateTimeOffset now) { Role = role; UpdatedAt = now; }
    public void SetActive(bool active, DateTimeOffset now) { IsActive = active; UpdatedAt = now; }
    public void SetPassword(string passwordHash, DateTimeOffset now) { PasswordHash = passwordHash; UpdatedAt = now; }
}

/// <summary>A hashed, expiring dashboard login session (P17). Only the SHA-256 hash is stored.</summary>
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

/// <summary>PBKDF2 (SHA-256) password hashing with a per-password salt; constant-time verify.</summary>
public static class PasswordHasher
{
    private const int Iterations = 100_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iter)) return false;
        var salt = Convert.FromBase64String(parts[1]);
        var expected = Convert.FromBase64String(parts[2]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iter, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
