using System.Security.Cryptography;
using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Members.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Members.Application;

public sealed record IssuedToken(string Token, DateTimeOffset? ExpiresAt);
public sealed record ResolvedMember(Guid ApplicationId, Guid MemberId);

/// <summary>
/// Issues and resolves member access tokens for the customer app (P11). Mirrors the API-key
/// handling: cryptographically random secret, only the SHA-256 hash is stored, plaintext returned
/// once. Issuance is tenant-admin scoped; resolution runs in the narrow system scope (the caller is
/// not yet authenticated) and returns the owning application + member.
/// </summary>
public sealed class MemberTokenService(LoyaltyDbContext db, ApplicationContext scope, IClock clock)
{
    private const string Prefix = "mt_";

    /// <summary>Admin-scoped: mint a token for one of the tenant's members.</summary>
    public async Task<IssuedToken?> IssueAsync(Guid memberId, TimeSpan? ttl, CancellationToken ct)
    {
        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == memberId, ct);
        if (member is null) return null;

        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        var raw = Prefix + Convert.ToHexStringLower(bytes);
        var now = clock.UtcNow;
        var expiresAt = ttl is { } t ? now.Add(t) : (DateTimeOffset?)null;

        db.MemberTokens.Add(new MemberToken(member.ApplicationId, member.Id, Hash(raw), expiresAt, now));
        await db.SaveChangesAsync(ct);
        return new IssuedToken(raw, expiresAt);
    }

    /// <summary>Admin-scoped: revoke (delete) all access tokens for one of the tenant's members.
    /// Returns the number of tokens removed, or null if the member does not exist.</summary>
    public async Task<int?> RevokeAllAsync(Guid memberId, CancellationToken ct)
    {
        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == memberId, ct);
        if (member is null) return null;

        var tokens = await db.MemberTokens.Where(t => t.MemberId == memberId).ToListAsync(ct);
        if (tokens.Count == 0) return 0;
        db.MemberTokens.RemoveRange(tokens);
        await db.SaveChangesAsync(ct);
        return tokens.Count;
    }

    /// <summary>System-scoped: resolve a plaintext token to its application + member, or null.</summary>
    public async Task<ResolvedMember?> ResolveAsync(string rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || !rawToken.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var hash = Hash(rawToken);
        scope.EnterSystemScope();
        try
        {
            var token = await db.MemberTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
            if (token is null || !token.IsValid(clock.UtcNow)) return null;
            return new ResolvedMember(token.ApplicationId, token.MemberId);
        }
        finally { scope.LeaveSystemScope(); }
    }

    private static string Hash(string raw) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));
}
