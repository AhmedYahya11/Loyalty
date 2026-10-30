using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Accounts.Domain;

/// <summary>
/// An API-credential identity. There is no separate ApiCredential entity: every account
/// row carries exactly one current API key, stored as a searchable prefix plus a
/// cryptographic hash (never the raw key). Rotating replaces prefix+hash atomically,
/// invalidating the previous key (MVP spec §5).
/// </summary>
public sealed class Account : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public string AccountName { get; private set; } = default!;
    public string KeyPrefix { get; private set; } = default!;
    public string KeyHash { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }
    public DateTimeOffset? LastAuthenticatedAt { get; private set; }

    private Account() { }

    public static Account Create(
        Guid id, Guid applicationId, string accountName,
        string keyPrefix, string keyHash, DateTimeOffset? expiresAt, DateTimeOffset now) => new()
    {
        Id = id,
        ApplicationId = applicationId,
        AccountName = accountName.Trim(),
        KeyPrefix = keyPrefix,
        KeyHash = keyHash,
        IsActive = true,
        CreatedAt = now,
        ExpiresAt = expiresAt,
    };

    public void RotateKey(string keyPrefix, string keyHash)
    {
        KeyPrefix = keyPrefix;
        KeyHash = keyHash;
    }

    public void Revoke(DateTimeOffset now)
    {
        IsActive = false;
        RevokedAt = now;
    }

    public void SetActive(bool active) => IsActive = active;

    public void MarkAuthenticated(DateTimeOffset now)
    {
        LastUsedAt = now;
        LastAuthenticatedAt = now;
    }

    /// <summary>Usable only when active, not revoked, and not expired.</summary>
    public bool IsUsable(DateTimeOffset now) =>
        IsActive && RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);
}
