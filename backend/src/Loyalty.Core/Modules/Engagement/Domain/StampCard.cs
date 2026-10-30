using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Engagement.Domain;

/// <summary>
/// A digital punch card (P10, chaay-style "buy N, get a reward"): each qualifying event adds one
/// stamp; reaching <see cref="StampsRequired"/> awards <see cref="RewardPoints"/> and the card
/// resets (keeping any remainder), so it is repeatable. Application-scoped configuration.
/// </summary>
public sealed class StampCard : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? NameAr { get; private set; }
    public string? Description { get; private set; }
    public string TriggerEventType { get; private set; } = default!;
    public int StampsRequired { get; private set; }
    public long RewardPoints { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private StampCard() { }

    public static StampCard Create(Guid applicationId, string name, string? nameAr, string? description,
        string? triggerEventType, int stampsRequired, long rewardPoints, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new LoyaltyException(LoyaltyError.ValidationFailed("name is required."));
        if (stampsRequired < 1) throw new LoyaltyException(LoyaltyError.ValidationFailed("stampsRequired must be >= 1."));
        if (rewardPoints < 0) throw new LoyaltyException(LoyaltyError.ValidationFailed("rewardPoints must be >= 0."));
        return new StampCard
        {
            Id = Guid.NewGuid(), ApplicationId = applicationId, Name = name.Trim(),
            NameAr = string.IsNullOrWhiteSpace(nameAr) ? null : nameAr.Trim(), Description = description,
            TriggerEventType = string.IsNullOrWhiteSpace(triggerEventType) ? "PurchaseCompleted" : triggerEventType.Trim(),
            StampsRequired = stampsRequired, RewardPoints = rewardPoints, IsActive = true, CreatedAt = now, UpdatedAt = now,
        };
    }

    public void Update(string name, string? nameAr, string? description, string? triggerEventType,
        int stampsRequired, long rewardPoints, bool isActive, DateTimeOffset now)
    {
        if (stampsRequired < 1) throw new LoyaltyException(LoyaltyError.ValidationFailed("stampsRequired must be >= 1."));
        Name = name.Trim(); NameAr = string.IsNullOrWhiteSpace(nameAr) ? null : nameAr.Trim(); Description = description;
        TriggerEventType = string.IsNullOrWhiteSpace(triggerEventType) ? TriggerEventType : triggerEventType.Trim();
        StampsRequired = stampsRequired; RewardPoints = rewardPoints; IsActive = isActive; UpdatedAt = now;
    }
}

/// <summary>A member's progress on a stamp card. UNIQUE(app, member, card).</summary>
public sealed class MemberStampCard : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid MemberId { get; private set; }
    public Guid StampCardId { get; private set; }
    public int Stamps { get; private set; }
    public int CompletedCount { get; private set; }
    public DateTimeOffset? LastStampedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private MemberStampCard() { }

    public MemberStampCard(Guid applicationId, Guid memberId, Guid stampCardId, DateTimeOffset now)
    {
        Id = Guid.NewGuid(); ApplicationId = applicationId; MemberId = memberId; StampCardId = stampCardId;
        CreatedAt = now; UpdatedAt = now;
    }

    /// <summary>Add one stamp; when the card is full, reset keeping the remainder and report completion.</summary>
    public bool AddStampAndCheckComplete(int stampsRequired, DateTimeOffset now)
    {
        Stamps++;
        LastStampedAt = now;
        UpdatedAt = now;
        if (Stamps >= stampsRequired)
        {
            Stamps -= stampsRequired;
            CompletedCount++;
            return true;
        }
        return false;
    }
}
