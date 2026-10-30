using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Cvm.Domain;

/// <summary>
/// One stored, application-scoped projection per member, built from accepted typed events and
/// committed ledger facts (MVP spec §11.2). Original events and ledger postings remain the
/// source of truth. Uses OccurredAt (not receipt time) so delayed delivery never moves activity
/// backward: FirstActivityAt = min, LastActivityAt = max.
/// </summary>
public sealed class MemberValueProfile : IApplicationScoped
{
    public Guid ApplicationId { get; private set; }
    public Guid MemberId { get; private set; }
    public DateTimeOffset? FirstActivityAt { get; private set; }
    public DateTimeOffset? LastActivityAt { get; private set; }
    public DateTimeOffset? LastPurchaseAt { get; private set; }
    public long PurchaseCount { get; private set; }
    public decimal NetSpendAmount { get; private set; }
    public string BaseCurrency { get; private set; } = default!;
    public long LoginCount { get; private set; }
    public long ReferralCount { get; private set; }
    public long PointsEarned { get; private set; }
    public long PointsRedeemed { get; private set; }
    public long PointsExpired { get; private set; }
    public LifecycleStage LifecycleStage { get; private set; }
    public DateTimeOffset? LastLifecycleEvaluationAt { get; private set; }
    public Guid? CurrentTierId { get; private set; }
    public int CurrentTierRank { get; private set; } = -1;
    public DateTimeOffset? TierEvaluatedAt { get; private set; }
    public int CurrentStreak { get; private set; }
    public int LongestStreak { get; private set; }
    public DateOnly? LastStreakDate { get; private set; }
    public int RfmRecency { get; private set; }
    public int RfmFrequency { get; private set; }
    public int RfmMonetary { get; private set; }
    public string? RfmPersona { get; private set; }
    public DateTimeOffset? RfmEvaluatedAt { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private MemberValueProfile() { }

    public static MemberValueProfile Create(Guid applicationId, Guid memberId, string baseCurrency, DateTimeOffset now) => new()
    {
        ApplicationId = applicationId,
        MemberId = memberId,
        FirstActivityAt = now,
        LastActivityAt = now,
        BaseCurrency = baseCurrency,
        LifecycleStage = LifecycleStage.New,
        Version = 1,
        CreatedAt = now,
        UpdatedAt = now,
    };

    private void Touch(DateTimeOffset occurredAt)
    {
        FirstActivityAt = FirstActivityAt is { } f ? (occurredAt < f ? occurredAt : f) : occurredAt;
        LastActivityAt = LastActivityAt is { } l ? (occurredAt > l ? occurredAt : l) : occurredAt;
        Version++;
        UpdatedAt = occurredAt;
    }

    /// <summary>CustomerRegistered: fill/earlier first activity; never increments twice.</summary>
    public void ApplyRegistration(DateTimeOffset occurredAt) => Touch(occurredAt);

    public void ApplyLogin(DateTimeOffset occurredAt) { LoginCount++; Touch(occurredAt); }

    public void ApplyPurchase(DateTimeOffset occurredAt, decimal amount)
    {
        PurchaseCount++;
        NetSpendAmount += amount;
        LastPurchaseAt = LastPurchaseAt is { } p ? (occurredAt > p ? occurredAt : p) : occurredAt;
        Touch(occurredAt);
    }

    public void ApplyRefund(decimal amount, DateTimeOffset occurredAt)
    {
        NetSpendAmount -= amount;
        if (NetSpendAmount < 0) NetSpendAmount = 0;
        Touch(occurredAt);
    }

    public void ApplyReferral(DateTimeOffset occurredAt) { ReferralCount++; Touch(occurredAt); }

    public void AddPointsEarned(long n) { PointsEarned += n; Version++; }
    public void AddPointsRedeemed(long n) { PointsRedeemed += n; Version++; }
    public void AddPointsExpired(long n) { PointsExpired += n; Version++; }

    public void SetLifecycle(LifecycleStage stage, DateTimeOffset evaluatedAt)
    {
        LifecycleStage = stage;
        LastLifecycleEvaluationAt = evaluatedAt;
        UpdatedAt = evaluatedAt;
    }

    /// <summary>Update the consecutive-day activity streak from an event's OccurredAt (idempotent per day).</summary>
    public void ApplyStreak(DateTimeOffset occurredAt)
    {
        var date = DateOnly.FromDateTime(occurredAt.UtcDateTime);
        if (LastStreakDate is null) CurrentStreak = 1;
        else if (date == LastStreakDate.Value) return;                 // same day — no change
        else if (date == LastStreakDate.Value.AddDays(1)) CurrentStreak++;  // consecutive
        else if (date > LastStreakDate.Value) CurrentStreak = 1;       // gap — reset
        else return;                                                   // out-of-order older event — ignore
        LastStreakDate = date;
        if (CurrentStreak > LongestStreak) LongestStreak = CurrentStreak;
        Version++;
    }

    public void SetTier(Guid? tierId, int rank, DateTimeOffset evaluatedAt)
    {
        CurrentTierId = tierId;
        CurrentTierRank = rank;
        TierEvaluatedAt = evaluatedAt;
        UpdatedAt = evaluatedAt;
    }

    /// <summary>Set the RFM scores (1–5) and derived persona label (P12).</summary>
    public void SetRfm(int recency, int frequency, int monetary, string persona, DateTimeOffset evaluatedAt)
    {
        RfmRecency = recency;
        RfmFrequency = frequency;
        RfmMonetary = monetary;
        RfmPersona = persona;
        RfmEvaluatedAt = evaluatedAt;
        UpdatedAt = evaluatedAt;
    }
}
