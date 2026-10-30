using System.Text.Json;
using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Integration.Contracts;
using Loyalty.Core.Modules.Integration.Domain;
using Loyalty.Core.Modules.Settings.Application;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wolverine.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Integration.Application;

public sealed record SubmitResult(int StatusCode, object Body, string? Location = null);

/// <summary>
/// The synchronous acceptance path for POST /api/v1/events (MVP spec §8, §9, PROCESSING_FLOW
/// diagram 1). Everything is validated before the transaction opens; then the idempotency
/// record, ingestion request, and durable Wolverine message commit together. Returns 202
/// only after that commit; a duplicate returns the original request id, not a 409.
/// </summary>
public sealed class EventIngestionService(
    LoyaltyDbContext db,
    IApplicationContext scope,
    EventRegistry registry,
    LoyaltySettingsService settings,
    Cvm.Application.CvmSettingsService cvmSettings,
    IDbContextOutbox<LoyaltyDbContext> outbox,
    IClock clock)
{
    private const string OperationName = "SubmitEvent";
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    public async Task<SubmitResult> SubmitAsync(
        LoyaltyEventEnvelopeV1 envelope, string rawPayloadJson, string? idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new LoyaltyException(LoyaltyError.ValidationFailed("Idempotency-Key header is required."));

        // 1. validate + normalize the typed contract (throws 400/422 before any write)
        var normalized = registry.Normalize(envelope, scope.SourceSystem);

        // 2. resolve + freeze effective loyalty settings
        var effective = await settings.ResolveEffectiveAsync(scope.ApplicationId, ct)
            ?? throw new LoyaltyException(LoyaltyError.LoyaltyNotConfigured());
        if (!effective.IsActiveAt(envelope.OccurredAt))
            throw new LoyaltyException(LoyaltyError.LoyaltyNotActive());

        // 3. HTTP idempotency (§9.1)
        var payloadHash = HashUtil.Sha256Hex(rawPayloadJson);
        var existingIdem = await db.IdempotencyRecords.FirstOrDefaultAsync(
            r => r.AccountId == scope.AccountId && r.OperationName == OperationName && r.IdempotencyKey == idempotencyKey, ct);
        if (existingIdem is not null)
        {
            if (existingIdem.RequestPayloadHash != payloadHash)
                throw new LoyaltyException(LoyaltyError.Conflict("IDEMPOTENCY_KEY_REUSED", "Idempotency key reused with a different payload."));
            return DuplicateResult(existingIdem.RequestId, await StatusOf(existingIdem.RequestId, ct));
        }

        // 3b. source-event / business-event identity pre-check (§9.2, §9.3)
        var priorByEvent = await db.IngestionRequests.FirstOrDefaultAsync(
            r => r.SourceSystem == scope.SourceSystem && r.EventId == envelope.EventId, ct)
            ?? await db.IngestionRequests.FirstOrDefaultAsync(
                r => r.EventType == envelope.EventType && r.BusinessEventKey == normalized.BusinessEventKey, ct);
        if (priorByEvent is not null)
            return AcceptedResult(priorByEvent.Id, priorByEvent.Status, duplicate: true);

        // 4. durable acceptance: idempotency record + ingestion request + durable message, one commit
        var requestId = Guid.NewGuid();
        var now = clock.UtcNow;
        var settingsSnapshot = JsonSerializer.Serialize(new
        {
            sourceApplicationId = effective.ApplicationId,
            version = effective.Version,
            effective.PointsUnitCode,
            effective.BaseCurrency,
            effective.SpendAmountPerPoint,
            roundingPolicy = effective.RoundingPolicy.ToString(),
            loyaltyStatus = effective.LoyaltyStatus.ToString(),
        }, JsonOptionsProvider.Default);

        // freeze the effective CVM settings snapshot too (null when CVM is not configured)
        var effectiveCvm = await cvmSettings.ResolveEffectiveAsync(scope.ApplicationId, ct);
        Guid? cvmAppId = effectiveCvm?.ApplicationId;
        int? cvmVersion = effectiveCvm?.Version;
        string? cvmSnapshot = effectiveCvm is null ? null : JsonSerializer.Serialize(new
        {
            sourceApplicationId = effectiveCvm.ApplicationId,
            version = effectiveCvm.Version,
            cvmStatus = effectiveCvm.CvmStatus.ToString(),
            effectiveCvm.NewPeriodDays,
            effectiveCvm.AtRiskAfterDays,
            effectiveCvm.DormantAfterDays,
            effectiveCvm.ChurnedAfterDays,
            effectiveCvm.HighValueNetSpendThreshold,
            effectiveCvm.FrequentPurchaseThreshold,
            effectiveCvm.EvaluationWindowDays,
        }, JsonOptionsProvider.Default);

        var request = IngestionRequest.Accept(
            requestId, scope.ApplicationId, scope.AccountId, scope.SourceSystem,
            envelope.EventId, envelope.EventType, envelope.EventVersion, envelope.SourceTransactionId,
            normalized.BusinessEventKey, envelope.CustomerId, envelope.OccurredAt, envelope.CorrelationId,
            rawPayloadJson, effective.ApplicationId, effective.Version, settingsSnapshot,
            cvmAppId, cvmVersion, cvmSnapshot, now);

        var responseBody = JsonSerializer.Serialize(
            AcceptedPayload(requestId, RequestStatus.Accepted, false), JsonOptionsProvider.Default);
        var idem = new IdempotencyRecord(
            Guid.NewGuid(), scope.ApplicationId, scope.AccountId, OperationName, idempotencyKey!,
            payloadHash, requestId, 202, responseBody, now, Retention);

        db.IngestionRequests.Add(request);
        db.IdempotencyRecords.Add(idem);

        await outbox.PublishAsync(new ProcessLoyaltyEvent(
            requestId, scope.ApplicationId, scope.AccountId, envelope.EventId, envelope.CorrelationId));

        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // lost a race with an identical concurrent submission: return the committed original
            var original = await db.IngestionRequests.AsNoTracking().FirstOrDefaultAsync(
                r => r.SourceSystem == scope.SourceSystem && r.EventId == envelope.EventId, ct);
            if (original is not null)
                return AcceptedResult(original.Id, original.Status, duplicate: true);
            throw new LoyaltyException(LoyaltyError.DurableAcceptanceFailed());
        }

        return AcceptedResult(requestId, RequestStatus.Accepted, duplicate: false);
    }

    public async Task<object?> GetRequestStatusAsync(Guid requestId, CancellationToken ct)
    {
        var r = await db.IngestionRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == requestId, ct);
        if (r is null) return null;
        return new
        {
            requestId = r.Id,
            status = r.Status.ToString(),
            eventType = r.EventType,
            customerId = r.CustomerId,
            awardedPoints = r.AwardedPoints,
            pointsUnitCode = r.PointsUnitCode,
            statusReason = r.StatusReason,
            completedAt = r.CompletedAt,
        };
    }

    private async Task<RequestStatus> StatusOf(Guid requestId, CancellationToken ct) =>
        await db.IngestionRequests.Where(r => r.Id == requestId).Select(r => r.Status).FirstOrDefaultAsync(ct);

    private static object AcceptedPayload(Guid requestId, RequestStatus status, bool duplicate) => new
    {
        requestId,
        status = status.ToString(),
        duplicate,
        statusUrl = $"/api/v1/requests/{requestId}",
    };

    private static SubmitResult AcceptedResult(Guid requestId, RequestStatus status, bool duplicate) =>
        new(202, AcceptedPayload(requestId, status, duplicate), $"/api/v1/requests/{requestId}");

    private static SubmitResult DuplicateResult(Guid requestId, RequestStatus status) =>
        new(202, AcceptedPayload(requestId, status, true), $"/api/v1/requests/{requestId}");

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
