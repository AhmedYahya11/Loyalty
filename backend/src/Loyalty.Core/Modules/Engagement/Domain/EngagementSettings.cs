using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Engagement.Domain;

/// <summary>
/// Per-application engagement configuration (P10): the referral program (reward both sides when a
/// referral completes) and the birthday reward (bonus points on a member's birthday). One row per
/// application, keyed by ApplicationId. Disabled programs are a no-op in the worker / sweep.
/// </summary>
public sealed class EngagementSettings : IApplicationScoped
{
    public Guid ApplicationId { get; private set; }
    public bool ReferralEnabled { get; private set; }
    public long ReferrerPoints { get; private set; }
    public long RefereePoints { get; private set; }
    public bool BirthdayEnabled { get; private set; }
    public long BirthdayBonusPoints { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private EngagementSettings() { }

    public static EngagementSettings Create(Guid applicationId, DateTimeOffset now) => new()
    {
        ApplicationId = applicationId,
        ReferralEnabled = false,
        ReferrerPoints = 0,
        RefereePoints = 0,
        BirthdayEnabled = false,
        BirthdayBonusPoints = 0,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void Update(bool referralEnabled, long referrerPoints, long refereePoints,
        bool birthdayEnabled, long birthdayBonusPoints, DateTimeOffset now)
    {
        if (referrerPoints < 0 || refereePoints < 0 || birthdayBonusPoints < 0)
            throw new LoyaltyException(LoyaltyError.ValidationFailed("points must be >= 0."));
        ReferralEnabled = referralEnabled;
        ReferrerPoints = referrerPoints;
        RefereePoints = refereePoints;
        BirthdayEnabled = birthdayEnabled;
        BirthdayBonusPoints = birthdayBonusPoints;
        UpdatedAt = now;
    }
}
