using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Engagement.Domain;

/// <summary>
/// A completed, rewarded referral (P10). One row per business referralId per application
/// (UNIQUE), so redelivery of a ReferralCompleted event never double-rewards. Links the referrer
/// (an existing member) to the referee (the new/qualifying member) and records the points paid to
/// each side out of the referral program.
/// </summary>
public sealed class Referral : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public string ReferralKey { get; private set; } = default!;   // the source referralId
    public Guid ReferrerMemberId { get; private set; }
    public Guid RefereeMemberId { get; private set; }
    public long ReferrerPointsAwarded { get; private set; }
    public long RefereePointsAwarded { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Referral() { }

    public Referral(Guid applicationId, string referralKey, Guid referrerMemberId, Guid refereeMemberId,
        long referrerPoints, long refereePoints, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        ApplicationId = applicationId;
        ReferralKey = referralKey.Trim();
        ReferrerMemberId = referrerMemberId;
        RefereeMemberId = refereeMemberId;
        ReferrerPointsAwarded = referrerPoints;
        RefereePointsAwarded = refereePoints;
        CreatedAt = now;
    }
}
