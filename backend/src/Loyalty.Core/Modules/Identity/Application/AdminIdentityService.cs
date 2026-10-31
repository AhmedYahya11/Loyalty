using System.Security.Cryptography;
using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Identity.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Identity.Application;

public sealed record LoginResult(string Token, DateTimeOffset ExpiresAt, string Username, string Role, Guid ApplicationId);
public sealed record ResolvedAdmin(Guid UserId, string Username, Guid ApplicationId, AdminRole Role);
public sealed record AdminUserView(Guid Id, Guid ApplicationId, string Username, string Role, bool IsActive, DateTimeOffset CreatedAt);
public sealed record CreateAdminUserInput(string Username, string Password, string Role, Guid? ApplicationId);

/// <summary>
/// Dashboard identity (P17): password login issuing hashed session tokens, session resolution for
/// the auth middleware, and user administration. Identity tables are not application-scoped, so this
/// works during anonymous login.
/// </summary>
public sealed class AdminIdentityService(LoyaltyDbContext db, IClock clock)
{
    private const string Prefix = "as_";
    private static readonly TimeSpan SessionTtl = TimeSpan.FromDays(7);

    public async Task<LoginResult?> LoginAsync(string username, string password, CancellationToken ct)
    {
        username = (username ?? "").Trim().ToLowerInvariant();
        var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Username == username && u.IsActive, ct);
        if (user is null || !PasswordHasher.Verify(password ?? "", user.PasswordHash)) return null;

        var raw = Prefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var now = clock.UtcNow;
        var expiresAt = now.Add(SessionTtl);
        db.AdminSessions.Add(new AdminSession(user.Id, Hash(raw), expiresAt, now));
        await db.SaveChangesAsync(ct);
        return new LoginResult(raw, expiresAt, user.Username, user.Role.ToString(), user.ApplicationId);
    }

    public async Task<ResolvedAdmin?> ResolveAsync(string rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || !rawToken.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var hash = Hash(rawToken);
        var session = await db.AdminSessions.FirstOrDefaultAsync(s => s.TokenHash == hash, ct);
        if (session is null || !session.IsValid(clock.UtcNow)) return null;
        var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Id == session.AdminUserId && u.IsActive, ct);
        return user is null ? null : new ResolvedAdmin(user.Id, user.Username, user.ApplicationId, user.Role);
    }

    /// <summary>
    /// Self-service password change (P17): verify the current password, set the new one, and revoke
    /// all of the user's other sessions (keeping the one making the request). Returns false when the
    /// current password is wrong.
    /// </summary>
    public async Task<bool> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, string? keepRawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            throw new LoyaltyException(LoyaltyError.ValidationFailed("new password must be at least 8 characters."));
        var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);
        if (user is null || !PasswordHasher.Verify(currentPassword ?? "", user.PasswordHash)) return false;

        var now = clock.UtcNow;
        user.SetPassword(PasswordHasher.Hash(newPassword), now);

        var keepHash = string.IsNullOrWhiteSpace(keepRawToken) ? null : Hash(keepRawToken);
        var sessions = await db.AdminSessions.Where(s => s.AdminUserId == userId && s.RevokedAt == null).ToListAsync(ct);
        foreach (var s in sessions.Where(s => s.TokenHash != keepHash)) s.Revoke(now);

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task RevokeAsync(string rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return;
        var hash = Hash(rawToken);
        var session = await db.AdminSessions.FirstOrDefaultAsync(s => s.TokenHash == hash, ct);
        if (session is not null) { session.Revoke(clock.UtcNow); await db.SaveChangesAsync(ct); }
    }

    /// <summary>Create a user. SuperAdmins may target any application + role; Admins create within
    /// their own application and cannot mint a SuperAdmin.</summary>
    public async Task<AdminUserView> CreateUserAsync(AdminContext caller, CreateAdminUserInput i, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(i.Username) || i.Username.Trim().Length < 3) throw new LoyaltyException(LoyaltyError.ValidationFailed("username must be at least 3 characters."));
        if (string.IsNullOrWhiteSpace(i.Password) || i.Password.Length < 8) throw new LoyaltyException(LoyaltyError.ValidationFailed("password must be at least 8 characters."));
        if (!Enum.TryParse<AdminRole>(i.Role, true, out var role)) throw new LoyaltyException(LoyaltyError.ValidationFailed("unknown role."));
        if (role == AdminRole.SuperAdmin && !caller.IsSuperAdmin) throw new LoyaltyException(LoyaltyError.Conflict("forbidden_role", "Only a SuperAdmin can create a SuperAdmin."));

        var appId = caller.IsSuperAdmin && i.ApplicationId is { } a ? a : caller.ApplicationId;
        var uname = i.Username.Trim().ToLowerInvariant();
        if (await db.AdminUsers.AnyAsync(u => u.Username == uname, ct))
            throw new LoyaltyException(LoyaltyError.Conflict("username_taken", "That username is already taken."));

        var user = AdminUser.Create(appId, uname, PasswordHasher.Hash(i.Password), role, clock.UtcNow);
        db.AdminUsers.Add(user);
        await db.SaveChangesAsync(ct);
        return ToView(user);
    }

    public async Task<IReadOnlyList<AdminUserView>> ListUsersAsync(AdminContext caller, CancellationToken ct)
    {
        var q = db.AdminUsers.AsQueryable();
        if (!caller.IsSuperAdmin) q = q.Where(u => u.ApplicationId == caller.ApplicationId); // Admins see their tenant only
        return (await q.OrderBy(u => u.Username).ToListAsync(ct)).Select(ToView).ToList();
    }

    public async Task<AdminUserView?> SetRoleAsync(AdminContext caller, Guid id, string role, CancellationToken ct)
    {
        if (!Enum.TryParse<AdminRole>(role, true, out var r)) throw new LoyaltyException(LoyaltyError.ValidationFailed("unknown role."));
        if (r == AdminRole.SuperAdmin && !caller.IsSuperAdmin) throw new LoyaltyException(LoyaltyError.Conflict("forbidden_role", "Only a SuperAdmin can grant SuperAdmin."));
        var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null || (!caller.IsSuperAdmin && user.ApplicationId != caller.ApplicationId)) return null;
        user.SetRole(r, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return ToView(user);
    }

    public async Task<bool> DeactivateAsync(AdminContext caller, Guid id, CancellationToken ct)
    {
        var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null || (!caller.IsSuperAdmin && user.ApplicationId != caller.ApplicationId)) return false;
        if (user.Id == caller.UserId) throw new LoyaltyException(LoyaltyError.Conflict("self_deactivate", "You cannot deactivate your own account."));
        user.SetActive(false, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Seed the first SuperAdmin for an application if no admin users exist at all (P17).</summary>
    public async Task EnsureSuperAdminAsync(Guid applicationId, string username, string password, CancellationToken ct)
    {
        if (await db.AdminUsers.AnyAsync(ct)) return;
        db.AdminUsers.Add(AdminUser.Create(applicationId, username, PasswordHasher.Hash(password), AdminRole.SuperAdmin, clock.UtcNow));
        await db.SaveChangesAsync(ct);
    }

    private static AdminUserView ToView(AdminUser u) => new(u.Id, u.ApplicationId, u.Username, u.Role.ToString(), u.IsActive, u.CreatedAt);
    private static string Hash(string raw) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));
}
