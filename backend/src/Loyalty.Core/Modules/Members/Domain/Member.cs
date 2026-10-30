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
    /// <summary>Shareable referral code, unique within the application (P10).</summary>
    public string? ReferralCode { get; private set; }
    /// <summary>Optional birthday, used by the birthday-reward sweep (P10). Date only; year ignored.</summary>
    public DateOnly? BirthDate { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Member() { }

    public static Member Create(
        Guid id, Guid applicationId, string externalCustomerId, Guid defaultWalletId, DateTimeOffset now,
        string? referralCode = null, DateOnly? birthDate = null) => new()
    {
        Id = id,
        ApplicationId = applicationId,
        ExternalCustomerId = externalCustomerId.Trim(),
        DefaultWalletId = defaultWalletId,
        Status = MemberStatus.Active,
        ReferralCode = referralCode,
        BirthDate = birthDate,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void SetBirthDate(DateOnly? birthDate, DateTimeOffset now) { BirthDate = birthDate; UpdatedAt = now; }
    public void SetReferralCode(string code, DateTimeOffset now) { ReferralCode = code; UpdatedAt = now; }
}
