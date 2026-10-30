using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Notifications.Domain;

/// <summary>Result-event types published by the platform (§14, §20).</summary>
public static class ResultEventTypes
{
    public const string PointsAwarded = "PointsAwardedV1";
    public const string PointsReversed = "PointsReversedV1";
    public const string MemberLifecycleChanged = "MemberLifecycleChangedV1";
    public const string MemberTierChanged = "MemberTierChangedV1";
    public const string PointsAdjusted = "PointsAdjustedV1";
    public const string RedemptionReserved = "RedemptionReservedV1";
    public const string PointsRedeemed = "PointsRedeemedV1";
    public const string RedemptionCancelled = "RedemptionCancelledV1";
}

/// <summary>
/// A durable result-event row written in the same transaction as the ledger change — a
/// hand-rolled transactional outbox (MVP spec §14). A background dispatcher delivers undelivered
/// entries to webhook subscriptions. Also the source for the notifications/event feed.
/// </summary>
public sealed class EventLogEntry : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public string EventType { get; private set; } = default!;
    public Guid? MemberId { get; private set; }
    public Guid? RequestId { get; private set; }
    public string Payload { get; private set; } = default!;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public int DeliveryAttempts { get; private set; }

    private EventLogEntry() { }

    public static EventLogEntry Create(
        Guid applicationId, string eventType, Guid? memberId, Guid? requestId, string payload,
        DateTimeOffset occurredAt, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ApplicationId = applicationId,
        EventType = eventType,
        MemberId = memberId,
        RequestId = requestId,
        Payload = payload,
        OccurredAt = occurredAt,
        CreatedAt = now,
    };

    public void MarkDelivered(DateTimeOffset now) { DeliveredAt = now; DeliveryAttempts++; }
    public void MarkAttempted() => DeliveryAttempts++;
}
