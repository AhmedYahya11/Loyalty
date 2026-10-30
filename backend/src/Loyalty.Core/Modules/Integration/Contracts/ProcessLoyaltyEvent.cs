namespace Loyalty.Core.Modules.Integration.Contracts;

/// <summary>
/// The durable Wolverine message enqueued transactionally with the ingestion request.
/// It carries everything the worker needs to restore application scope and process the
/// event (MVP spec §14). Transport-neutral so Kafka can replace local transport later.
/// </summary>
public sealed record ProcessLoyaltyEvent(
    Guid RequestId,
    Guid ApplicationId,
    Guid AccountId,
    Guid EventId,
    string CorrelationId);
