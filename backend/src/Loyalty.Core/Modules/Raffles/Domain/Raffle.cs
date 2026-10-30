using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Campaigns.Domain;

namespace Loyalty.Core.Modules.Raffles.Domain;

public enum RaffleStatus { Open = 0, Drawn = 1, Cancelled = 2 }

/// <summary>
/// A prize draw (P15): qualifying events earn a member weighted entries ("vouchers"); the more a
/// member qualifies, the higher their odds. A draw picks a weighted-random winner and awards the
/// prize. Fully tenant-configurable — trigger event, optional attribute conditions (reuses the
/// campaign condition model + rule evaluator), an optional minimum purchase amount, entries granted
/// per qualifying event, and the prize (points + description).
/// </summary>
public sealed class Raffle : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? NameAr { get; private set; }
    public string? Description { get; private set; }
    public string TriggerEventType { get; private set; } = default!;
    public List<CampaignCondition> Conditions { get; private set; } = new();
    public decimal MinAmount { get; private set; }         // for purchase triggers: only amounts >= this qualify
    public int EntriesPerEvent { get; private set; }
    public long PrizePoints { get; private set; }
    public string? PrizeDescription { get; private set; }
    public DateTimeOffset? DrawAtUtc { get; private set; }
    public RaffleStatus Status { get; private set; }
    public Guid? WinnerMemberId { get; private set; }
    public DateTimeOffset? DrawnAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Raffle() { }

    public static Raffle Create(Guid applicationId, string name, string? nameAr, string? description,
        string? triggerEventType, List<CampaignCondition>? conditions, decimal minAmount, int entriesPerEvent,
        long prizePoints, string? prizeDescription, DateTimeOffset? drawAt, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new LoyaltyException(LoyaltyError.ValidationFailed("name is required."));
        if (entriesPerEvent < 1) throw new LoyaltyException(LoyaltyError.ValidationFailed("entriesPerEvent must be >= 1."));
        if (prizePoints < 0) throw new LoyaltyException(LoyaltyError.ValidationFailed("prizePoints must be >= 0."));
        return new Raffle
        {
            Id = Guid.NewGuid(), ApplicationId = applicationId, Name = name.Trim(),
            NameAr = string.IsNullOrWhiteSpace(nameAr) ? null : nameAr.Trim(), Description = description,
            TriggerEventType = string.IsNullOrWhiteSpace(triggerEventType) ? "PurchaseCompleted" : triggerEventType.Trim(),
            Conditions = conditions ?? new(), MinAmount = minAmount < 0 ? 0 : minAmount,
            EntriesPerEvent = entriesPerEvent, PrizePoints = prizePoints,
            PrizeDescription = string.IsNullOrWhiteSpace(prizeDescription) ? null : prizeDescription.Trim(),
            DrawAtUtc = drawAt, Status = RaffleStatus.Open, CreatedAt = now, UpdatedAt = now,
        };
    }

    public bool AcceptsEntries => Status == RaffleStatus.Open;

    /// <summary>A purchase qualifies only if its amount clears the minimum (non-purchase triggers ignore it).</summary>
    public bool AmountQualifies(decimal? amount) => MinAmount <= 0 || (amount ?? 0) >= MinAmount;

    public void MarkDrawn(Guid? winnerMemberId, DateTimeOffset now)
    {
        Status = RaffleStatus.Drawn;
        WinnerMemberId = winnerMemberId;
        DrawnAt = now;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now) { Status = RaffleStatus.Cancelled; UpdatedAt = now; }
}

/// <summary>
/// A member's raffle entries from one source event (P15). UNIQUE(app, raffle, sourceEvent) makes
/// redelivery a no-op; a member's odds are the SUM of their <see cref="Count"/> across rows.
/// </summary>
public sealed class RaffleEntry : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid RaffleId { get; private set; }
    public Guid MemberId { get; private set; }
    public int Count { get; private set; }
    public Guid SourceEventId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private RaffleEntry() { }

    public RaffleEntry(Guid applicationId, Guid raffleId, Guid memberId, int count, Guid sourceEventId, DateTimeOffset now)
    {
        Id = Guid.NewGuid(); ApplicationId = applicationId; RaffleId = raffleId; MemberId = memberId;
        Count = count; SourceEventId = sourceEventId; CreatedAt = now;
    }
}
