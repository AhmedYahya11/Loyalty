using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Gamification.Domain;

/// <summary>
/// A time-boxed goal: accumulate a metric to a target within [StartsAtUtc, EndsAtUtc] to earn
/// bonus points. Progress is tracked per member (unlike cumulative achievements).
/// </summary>
public sealed class Challenge : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? NameAr { get; private set; }
    public string? Description { get; private set; }
    public GamificationMetric Metric { get; private set; }
    public decimal TargetValue { get; private set; }
    public DateTimeOffset StartsAtUtc { get; private set; }
    public DateTimeOffset EndsAtUtc { get; private set; }
    public long BonusPoints { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Challenge() { }

    public static Challenge Create(Guid applicationId, string name, string? nameAr, string? description,
        GamificationMetric metric, decimal targetValue, DateTimeOffset startsAt, DateTimeOffset endsAt, long bonusPoints, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new LoyaltyException(LoyaltyError.ValidationFailed("name is required."));
        if (targetValue <= 0) throw new LoyaltyException(LoyaltyError.ValidationFailed("targetValue must be > 0."));
        if (endsAt <= startsAt) throw new LoyaltyException(LoyaltyError.ValidationFailed("endsAtUtc must be after startsAtUtc."));
        return new Challenge
        {
            Id = Guid.NewGuid(), ApplicationId = applicationId, Name = name.Trim(),
            NameAr = string.IsNullOrWhiteSpace(nameAr) ? null : nameAr.Trim(), Description = description,
            Metric = metric, TargetValue = targetValue, StartsAtUtc = startsAt, EndsAtUtc = endsAt,
            BonusPoints = bonusPoints, IsActive = true, CreatedAt = now,
        };
    }

    public bool IsOpen(DateTimeOffset at) => IsActive && at >= StartsAtUtc && at <= EndsAtUtc;

    /// <summary>The amount this event contributes to the challenge metric.</summary>
    public decimal Contribution(string eventType, decimal? amount) => Metric switch
    {
        GamificationMetric.PurchaseCount => eventType == Integration.Contracts.EventTypes.PurchaseCompleted ? 1 : 0,
        GamificationMetric.NetSpend => eventType == Integration.Contracts.EventTypes.PurchaseCompleted ? (amount ?? 0) : 0,
        GamificationMetric.LoginCount => eventType == Integration.Contracts.EventTypes.ApplicationLogin ? 1 : 0,
        GamificationMetric.ReferralCount => eventType == Integration.Contracts.EventTypes.ReferralCompleted ? 1 : 0,
        _ => 0,
    };
}

/// <summary>Per-member progress toward a challenge. UNIQUE(app, member, challenge).</summary>
public sealed class MemberChallengeProgress : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid MemberId { get; private set; }
    public Guid ChallengeId { get; private set; }
    public decimal Progress { get; private set; }
    public bool Completed { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private MemberChallengeProgress() { }

    public MemberChallengeProgress(Guid applicationId, Guid memberId, Guid challengeId, DateTimeOffset now)
    {
        Id = Guid.NewGuid(); ApplicationId = applicationId; MemberId = memberId; ChallengeId = challengeId; UpdatedAt = now;
    }

    public void Add(decimal amount, DateTimeOffset now) { Progress += amount; UpdatedAt = now; }
    public void Complete(DateTimeOffset now) { Completed = true; CompletedAt = now; UpdatedAt = now; }
}
