using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Members.Domain;

public enum MemberStatus { Active = 0, Suspended = 1, Closed = 2 }

/// <summary>
/// A loyalty member, identified within its application by ExternalCustomerId (e.g. the
/// Hareer User.id). Every member has exactly one mandatory default wallet; the member's
/// balance lives on that wallet, never duplicated here (MVP spec §10).
/// </summary>
public sealed class Member : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public string ExternalCustomerId { get; private set; } = default!;
    public Guid DefaultWalletId { get; private set; }
    public MemberStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Member() { }

    public static Member Create(
        Guid id, Guid applicationId, string externalCustomerId, Guid defaultWalletId, DateTimeOffset now) => new()
    {
        Id = id,
        ApplicationId = applicationId,
        ExternalCustomerId = externalCustomerId.Trim(),
        DefaultWalletId = defaultWalletId,
        Status = MemberStatus.Active,
        CreatedAt = now,
        UpdatedAt = now,
    };
}
