using System.Security.Cryptography;
using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Identity.Application;

public sealed record LoginResult(string Token, DateTimeOffset ExpiresAt, string Username, string Role, Guid ApplicationId);
public sealed record ResolvedAdmin(Guid UserId, string Username, Guid ApplicationId, AdminRole Role);
public sealed record AdminUserView(Guid Id, Guid ApplicationId, string Username, string Role, bool IsActive, DateTimeOffset CreatedAt);
public sealed record CreateAdminUserInput(string Username, string Password, string Role, Guid? ApplicationId);

/// <summary>
/// Dashboard identity (P17), backed by ASP.NET Core Identity. <see cref="UserManager{TUser}"/> owns
/// users + password hashing and <see cref="RoleManager{TRole}"/> owns the four roles; this service
/// adds the multi-tenant rules (tenant-scoped admins, root SuperAdmin) and mints the server-side
/// <c>as_</c> bearer session the auth middleware resolves. User/role tables are not application-scoped,
/// so login (anonymous) works.
/// </summary>
public sealed class AdminIdentityService(
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole<Guid>> roles,
    IRepository<AdminSession> adminSessionsRepo,
    IUnitOfWork uow,
    IClock clock)
{
    private const string Prefix = "as_";
    private static readonly TimeSpan SessionTtl = TimeSpan.FromDays(7);

    public async Task<LoginResult?> LoginAsync(string username, string password, CancellationToken ct)
    {
        var user = await users.FindByNameAsync((username ?? "").Trim());
        if (user is null || !user.IsActive || !await users.CheckPasswordAsync(user, password ?? "")) return null;

        var raw = Prefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var now = clock.UtcNow;
        var expiresAt = now.Add(SessionTtl);
        adminSessionsRepo.Add(new AdminSession(user.Id, Hash(raw), expiresAt, now));
        await uow.SaveChangesAsync(ct);
        return new LoginResult(raw, expiresAt, user.UserName!, await RoleOf(user), user.ApplicationId);
    }

    public async Task<ResolvedAdmin?> ResolveAsync(string rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || !rawToken.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var hash = Hash(rawToken);
        var session = await adminSessionsRepo.Query().FirstOrDefaultAsync(s => s.TokenHash == hash, ct);
        if (session is null || !session.IsValid(clock.UtcNow)) return null;
        var user = await users.FindByIdAsync(session.AdminUserId.ToString());
        if (user is null || !user.IsActive) return null;
        return new ResolvedAdmin(user.Id, user.UserName!, user.ApplicationId, ParseRole(await RoleOf(user)));
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
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive) return false;
        var result = await users.ChangePasswordAsync(user, currentPassword ?? "", newPassword);
        if (!result.Succeeded) return false;

        var now = clock.UtcNow;
        var keepHash = string.IsNullOrWhiteSpace(keepRawToken) ? null : Hash(keepRawToken);
        var sessions = await adminSessionsRepo.Query().Where(s => s.AdminUserId == userId && s.RevokedAt == null).ToListAsync(ct);
        foreach (var s in sessions.Where(s => s.TokenHash != keepHash)) s.Revoke(now);
        await uow.SaveChangesAsync(ct);
        return true;
    }

    public async Task RevokeAsync(string rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return;
        var hash = Hash(rawToken);
        var session = await adminSessionsRepo.Query().FirstOrDefaultAsync(s => s.TokenHash == hash, ct);
        if (session is not null) { session.Revoke(clock.UtcNow); await uow.SaveChangesAsync(ct); }
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
        if (await users.FindByNameAsync(uname) is not null)
            throw new LoyaltyException(LoyaltyError.Conflict("username_taken", "That username is already taken."));

        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = uname, ApplicationId = appId, IsActive = true, CreatedAt = clock.UtcNow };
        var created = await users.CreateAsync(user, i.Password);
        if (!created.Succeeded) throw new LoyaltyException(LoyaltyError.ValidationFailed(string.Join("; ", created.Errors.Select(x => x.Description))));
        await EnsureRoleAsync(role);
        await users.AddToRoleAsync(user, role.ToString());
        return new AdminUserView(user.Id, user.ApplicationId, user.UserName!, role.ToString(), user.IsActive, user.CreatedAt);
    }

    public async Task<IReadOnlyList<AdminUserView>> ListUsersAsync(AdminContext caller, CancellationToken ct)
    {
        var q = users.Users;
        if (!caller.IsSuperAdmin) q = q.Where(u => u.ApplicationId == caller.ApplicationId); // Admins see their tenant only
        var list = await q.OrderBy(u => u.UserName).ToListAsync(ct);
        var views = new List<AdminUserView>(list.Count);
        foreach (var u in list)
            views.Add(new AdminUserView(u.Id, u.ApplicationId, u.UserName!, await RoleOf(u), u.IsActive, u.CreatedAt));
        return views;
    }

    public async Task<AdminUserView?> SetRoleAsync(AdminContext caller, Guid id, string role, CancellationToken ct)
    {
        if (!Enum.TryParse<AdminRole>(role, true, out var r)) throw new LoyaltyException(LoyaltyError.ValidationFailed("unknown role."));
        if (r == AdminRole.SuperAdmin && !caller.IsSuperAdmin) throw new LoyaltyException(LoyaltyError.Conflict("forbidden_role", "Only a SuperAdmin can grant SuperAdmin."));
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null || (!caller.IsSuperAdmin && user.ApplicationId != caller.ApplicationId)) return null;

        var current = await users.GetRolesAsync(user);
        if (current.Count > 0) await users.RemoveFromRolesAsync(user, current);
        await EnsureRoleAsync(r);
        await users.AddToRoleAsync(user, r.ToString());
        return new AdminUserView(user.Id, user.ApplicationId, user.UserName!, r.ToString(), user.IsActive, user.CreatedAt);
    }

    public async Task<bool> DeactivateAsync(AdminContext caller, Guid id, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null || (!caller.IsSuperAdmin && user.ApplicationId != caller.ApplicationId)) return false;
        if (user.Id == caller.UserId) throw new LoyaltyException(LoyaltyError.Conflict("self_deactivate", "You cannot deactivate your own account."));
        user.IsActive = false;
        await users.UpdateAsync(user);
        return true;
    }

    /// <summary>Seed the four roles and the first SuperAdmin if no users exist at all (P17).</summary>
    public async Task EnsureSuperAdminAsync(Guid applicationId, string username, string password, CancellationToken ct)
    {
        foreach (var r in Enum.GetValues<AdminRole>()) await EnsureRoleAsync(r);
        if (await users.Users.AnyAsync(ct)) return;
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = username.Trim().ToLowerInvariant(), ApplicationId = applicationId, IsActive = true, CreatedAt = clock.UtcNow };
        var created = await users.CreateAsync(user, password);
        if (created.Succeeded) await users.AddToRoleAsync(user, AdminRole.SuperAdmin.ToString());
    }

    private async Task EnsureRoleAsync(AdminRole role)
    {
        var name = role.ToString();
        if (!await roles.RoleExistsAsync(name))
            await roles.CreateAsync(new IdentityRole<Guid>(name) { Id = Guid.NewGuid() });
    }

    private async Task<string> RoleOf(ApplicationUser user) =>
        (await users.GetRolesAsync(user)).FirstOrDefault() ?? AdminRole.Viewer.ToString();

    private static AdminRole ParseRole(string role) => Enum.TryParse<AdminRole>(role, true, out var r) ? r : AdminRole.Viewer;
    private static string Hash(string raw) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));
}
