using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Integration.Domain;

/// <summary>
/// HTTP request idempotency (MVP spec §9.1). UNIQUE(ApplicationId, AccountId, OperationName,
/// IdempotencyKey). Same key + same payload hash returns the original result; same key +
/// different payload hash is a 409 IDEMPOTENCY_KEY_REUSED.
/// </summary>
public sealed class IdempotencyRecord : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid AccountId { get; private set; }
    public string OperationName { get; private set; } = default!;
    public string IdempotencyKey { get; private set; } = default!;
    public string RequestPayloadHash { get; private set; } = default!;
    public Guid RequestId { get; private set; }
    public int ResponseStatusCode { get; private set; }
    public string ResponseBody { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    private IdempotencyRecord() { }

    public IdempotencyRecord(
        Guid id, Guid applicationId, Guid accountId, string operationName, string idempotencyKey,
        string requestPayloadHash, Guid requestId, int responseStatusCode, string responseBody,
        DateTimeOffset now, TimeSpan retention)
    {
        Id = id;
        ApplicationId = applicationId;
        AccountId = accountId;
        OperationName = operationName;
        IdempotencyKey = idempotencyKey;
        RequestPayloadHash = requestPayloadHash;
        RequestId = requestId;
        ResponseStatusCode = responseStatusCode;
        ResponseBody = responseBody;
        CreatedAt = now;
        ExpiresAt = now.Add(retention);
    }
}
