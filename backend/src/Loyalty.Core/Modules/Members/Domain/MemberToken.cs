using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Members.Domain;

/// <summary>
/// A member-scoped access token for the customer-facing app (P11). Only a SHA-256 hash is stored;
/// the plaintext (prefix <c>mt_</c>) is returned once at issuance. A token authenticates exactly one
/// member and grants read + self-service access to that member's own data only — never the tenant
/// admin surface. Tokens can expire and be revoked.
/// </summary>
public sealed class MemberToken : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid MemberId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    private MemberToken() { }

    public MemberToken(Guid applicationId, Guid memberId, string tokenHash, DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        ApplicationId = applicationId;
        MemberId = memberId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        CreatedAt = now;
    }

    public bool IsValid(DateTimeOffset now) => RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);
    public void Revoke(DateTimeOffset now) => RevokedAt = now;
}
