using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Rewards.Domain;

public enum RedemptionStatus { Reserved = 0, Confirmed = 1, Cancelled = 2, Expired = 3 }

/// <summary>
/// A member's redemption of points for a reward, following reserve→confirm→cancel (MVP spec
/// §redemption). Reserve holds the points (active→reserved) and issues a coupon code (the QR
/// payload); confirm spends them; cancel/expire releases them.
/// </summary>
public sealed class Redemption : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid MemberId { get; private set; }
    public Guid RewardId { get; private set; }
    public long PointsCost { get; private set; }
    public RedemptionStatus Status { get; private set; }
    public string CouponCode { get; private set; } = default!;
    public DateTimeOffset ReservedAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    private Redemption() { }

    public static Redemption Reserve(
        Guid applicationId, Guid memberId, Guid rewardId, long pointsCost, string couponCode,
        DateTimeOffset now, TimeSpan hold) => new()
    {
        Id = Guid.NewGuid(),
        ApplicationId = applicationId,
        MemberId = memberId,
        RewardId = rewardId,
        PointsCost = pointsCost,
        Status = RedemptionStatus.Reserved,
        CouponCode = couponCode,
        ReservedAt = now,
        ExpiresAt = now.Add(hold),
    };

    public void Confirm(DateTimeOffset now) { Status = RedemptionStatus.Confirmed; ConfirmedAt = now; }
    public void Cancel(DateTimeOffset now) { Status = RedemptionStatus.Cancelled; CancelledAt = now; }
    public void Expire(DateTimeOffset now) { Status = RedemptionStatus.Expired; CancelledAt = now; }

    public bool IsHeld => Status == RedemptionStatus.Reserved;
}
