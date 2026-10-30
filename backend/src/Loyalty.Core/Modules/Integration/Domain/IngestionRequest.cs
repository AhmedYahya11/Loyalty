using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Integration.Domain;

public enum RequestStatus
{
    Accepted = 0,
    Queued = 1,
    Processing = 2,
    Retrying = 3,
    Completed = 4,
    Rejected = 5,
    Failed = 6,
    DeadLettered = 7,
}

/// <summary>
/// The durable record of one accepted loyalty event and its processing lifecycle. The
/// effective loyalty-settings snapshot is frozen here at acceptance so retries use the same
/// values even if ancestor settings later change (MVP spec §8, §10).
/// </summary>
public sealed class IngestionRequest : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid AccountId { get; private set; }
    public string SourceSystem { get; private set; } = default!;
    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = default!;
    public int EventVersion { get; private set; }
    public string SourceTransactionId { get; private set; } = default!;
    public string BusinessEventKey { get; private set; } = default!;
    public string CustomerId { get; private set; } = default!;
    public DateTimeOffset OccurredAt { get; private set; }
    public string CorrelationId { get; private set; } = default!;
    public string Payload { get; private set; } = default!;
    public RequestStatus Status { get; private set; }
    public string? StatusReason { get; private set; }

    // frozen effective settings snapshot
    public Guid ResolvedSettingsApplicationId { get; private set; }
    public int SettingsVersion { get; private set; }
    public string SettingsSnapshot { get; private set; } = default!;

    // result
    public long? AwardedPoints { get; private set; }
    public string? PointsUnitCode { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private IngestionRequest() { }

    public static IngestionRequest Accept(
        Guid id, Guid applicationId, Guid accountId, string sourceSystem,
        Guid eventId, string eventType, int eventVersion, string sourceTransactionId,
        string businessEventKey, string customerId, DateTimeOffset occurredAt, string correlationId,
        string payload, Guid resolvedSettingsApplicationId, int settingsVersion, string settingsSnapshot,
        DateTimeOffset now) => new()
    {
        Id = id,
        ApplicationId = applicationId,
        AccountId = accountId,
        SourceSystem = sourceSystem,
        EventId = eventId,
        EventType = eventType,
        EventVersion = eventVersion,
        SourceTransactionId = sourceTransactionId,
        BusinessEventKey = businessEventKey,
        CustomerId = customerId,
        OccurredAt = occurredAt,
        CorrelationId = correlationId,
        Payload = payload,
        Status = RequestStatus.Accepted,
        ResolvedSettingsApplicationId = resolvedSettingsApplicationId,
        SettingsVersion = settingsVersion,
        SettingsSnapshot = settingsSnapshot,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void MarkProcessing(DateTimeOffset now) { Status = RequestStatus.Processing; UpdatedAt = now; }

    public void MarkCompleted(long awardedPoints, string pointsUnitCode, DateTimeOffset now)
    {
        Status = RequestStatus.Completed;
        AwardedPoints = awardedPoints;
        PointsUnitCode = pointsUnitCode;
        CompletedAt = now;
        UpdatedAt = now;
    }

    public void MarkRejected(string reason, DateTimeOffset now)
    {
        Status = RequestStatus.Rejected;
        StatusReason = reason;
        UpdatedAt = now;
    }

    public void MarkFailed(string reason, DateTimeOffset now)
    {
        Status = RequestStatus.Failed;
        StatusReason = reason;
        UpdatedAt = now;
    }
}
