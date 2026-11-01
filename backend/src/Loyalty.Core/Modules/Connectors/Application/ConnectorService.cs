using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Applications.Application;
using Loyalty.Core.Modules.Connectors.Domain;
using Loyalty.Core.Modules.Integration.Application;
using Loyalty.Core.Modules.Integration.Contracts;
using Loyalty.Core.Modules.Integration.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Connectors.Application;

public sealed record ConnectorView(Guid Id, string Platform, bool IsActive, DateTimeOffset CreatedAt, string WebhookPath, string? Secret);

/// <summary>
/// Inbound e-commerce / POS connectors (B2). Admins create a per-tenant connector (which mints an HMAC
/// secret); the platform then posts signed webhooks to the connector's URL. The webhook handler
/// authenticates by HMAC, resolves + isolates the tenant, maps the payload to a loyalty event, and
/// ingests it through the normal pipeline (so idempotency, validation, logging and replay all apply).
/// </summary>
public sealed class ConnectorService(
    IRepository<Connector> connectorsRepo,
    IRepository<IngestionRequest> ingestionRequestsRepo,
    ApplicationContext scope,
    ApplicationScopeResolver resolver,
    EventIngestionService ingestion,
    IUnitOfWork uow,
    IClock clock)
{
    // ---------- admin (tenant-scoped) ----------

    public async Task<ConnectorView> CreateAsync(string platform, string? secret, CancellationToken ct)
    {
        if (!Enum.TryParse<ConnectorPlatform>(platform, true, out var p))
            throw new LoyaltyException(LoyaltyError.ValidationFailed("unknown connector platform."));
        var key = string.IsNullOrWhiteSpace(secret) ? GenerateSecret() : secret.Trim();
        var connector = Connector.Create(scope.ApplicationId, p, key, clock.UtcNow);
        connectorsRepo.Add(connector);
        await uow.SaveChangesAsync(ct);
        return ToView(connector, revealSecret: true); // secret returned once, on creation
    }

    public async Task<IReadOnlyList<ConnectorView>> ListAsync(CancellationToken ct) =>
        (await connectorsRepo.Query().AsNoTracking().OrderByDescending(c => c.CreatedAt).ToListAsync(ct))
            .Select(c => ToView(c, revealSecret: false)).ToList();

    // ---------- inbound webhook (anonymous; authenticated by HMAC) ----------

    /// <summary>Handle a Shopify webhook: verify HMAC, isolate the tenant, map + ingest. Returns the
    /// ingestion result (or a 401/400). Supports orders/paid -> PurchaseCompleted and
    /// refunds/create -> OrderRefunded (customer resolved from the original order).</summary>
    public async Task<SubmitResult> HandleShopifyWebhookAsync(
        Guid connectorId, string? topic, string? webhookId, byte[] rawBody, string? hmacHeader, CancellationToken ct)
    {
        // resolve the connector in system scope (the webhook is anonymous, no tenant established yet)
        scope.EnterSystemScope();
        Connector? connector;
        try { connector = await connectorsRepo.Query().FirstOrDefaultAsync(c => c.Id == connectorId, ct); }
        finally { scope.LeaveSystemScope(); }

        // uniform 401 for unknown/disabled connector or bad signature (no enumeration)
        if (connector is null || !connector.IsActive || !VerifyHmac(rawBody, connector.Secret, hmacHeader))
            return Unauthorized();

        // tenant isolation: establish this connector's application scope for everything downstream
        if (!await resolver.IsActiveChainAsync(connector.ApplicationId, ct)) return Unauthorized();
        var visible = await resolver.ResolveVisibleAsync(connector.ApplicationId, ct);
        scope.Establish(connector.ApplicationId, connector.Id, "shopify", visible);

        using var doc = Parse(rawBody);
        var root = doc.RootElement;
        var envelope = (topic ?? "").ToLowerInvariant() switch
        {
            "orders/paid" or "orders/create" => MapPurchase(root),
            "refunds/create" => await MapRefundAsync(root, ct),
            _ => null, // unhandled topic — acknowledge without ingesting
        };
        if (envelope is null) return new SubmitResult(200, new { ignored = true, topic });

        // idempotency: Shopify's delivery id dedups retries; fall back to the business key
        var idempotencyKey = string.IsNullOrWhiteSpace(webhookId) ? $"shopify:{envelope.EventType}:{envelope.SourceTransactionId}" : $"shopify-whk:{webhookId}";
        var canonical = JsonSerializer.Serialize(envelope, JsonOptionsProvider.Default);
        return await ingestion.SubmitAsync(envelope, canonical, idempotencyKey, ct);
    }

    // ---------- mapping ----------

    private static LoyaltyEventEnvelopeV1 MapPurchase(JsonElement order)
    {
        var orderId = NumOrStr(order, "id") ?? throw Invalid("order id is required.");
        var amount = DecProp(order, "total_price") ?? throw Invalid("total_price is required.");
        var currency = Str(order, "currency") ?? "USD";
        var customerId = CustomerId(order) ?? throw Invalid("order customer is required.");
        var occurredAt = DateProp(order, "processed_at") ?? DateProp(order, "created_at") ?? DateTimeOffset.UtcNow;
        return new LoyaltyEventEnvelopeV1(
            Guid.NewGuid(), "PurchaseCompleted", 1, orderId, customerId, occurredAt, orderId,
            Attrs(new { orderId, amount, currency }));
    }

    private async Task<LoyaltyEventEnvelopeV1> MapRefundAsync(JsonElement refund, CancellationToken ct)
    {
        var refundId = NumOrStr(refund, "id") ?? throw Invalid("refund id is required.");
        var orderId = NumOrStr(refund, "order_id") ?? throw Invalid("refund order_id is required.");
        var amount = RefundAmount(refund) ?? throw Invalid("refund amount is required.");

        // Shopify refund payloads omit the customer — resolve it from the original purchase we ingested.
        var customerId = await ingestionRequestsRepo.Query().AsNoTracking()
            .Where(r => r.EventType == "PurchaseCompleted" && r.SourceTransactionId == orderId)
            .Select(r => r.CustomerId).FirstOrDefaultAsync(ct)
            ?? throw Invalid($"no original order '{orderId}' found to attribute this refund.");

        return new LoyaltyEventEnvelopeV1(
            Guid.NewGuid(), "OrderRefunded", 1, refundId, customerId, DateTimeOffset.UtcNow, refundId,
            Attrs(new { refundId, originalOrderId = orderId, amount, currency = Str(refund, "currency") ?? "USD" }));
    }

    // ---------- helpers ----------

    private static bool VerifyHmac(byte[] rawBody, string secret, string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return false;
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = Convert.ToBase64String(h.ComputeHash(rawBody));
        var a = Encoding.UTF8.GetBytes(computed);
        var b = Encoding.UTF8.GetBytes(header.Trim());
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static string GenerateSecret() => "whsec_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));

    private static JsonDocument Parse(byte[] body)
    {
        try { return JsonDocument.Parse(body); }
        catch (JsonException) { throw Invalid("webhook body is not valid JSON."); }
    }

    private ConnectorView ToView(Connector c, bool revealSecret) => new(
        c.Id, c.Platform.ToString(), c.IsActive, c.CreatedAt,
        $"/api/v1/connectors/{c.Platform.ToString().ToLowerInvariant()}/{c.Id}/webhook",
        revealSecret ? c.Secret : null);

    private static JsonElement Attrs(object o) =>
        JsonSerializer.SerializeToElement(o, JsonOptionsProvider.Default);

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? NumOrStr(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), _ => null };
    }

    private static decimal? DecProp(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDecimal();
        if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
        return null;
    }

    private static DateTimeOffset? DateProp(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(v.GetString(), out var d) ? d : null;

    private static string? CustomerId(JsonElement order)
    {
        if (order.TryGetProperty("customer", out var c) && c.ValueKind == JsonValueKind.Object)
            return NumOrStr(c, "id") ?? Str(c, "email");
        return Str(order, "email");
    }

    /// <summary>Sum the refund's transaction amounts (Shopify carries the refunded value there).</summary>
    private static decimal? RefundAmount(JsonElement refund)
    {
        if (!refund.TryGetProperty("transactions", out var txns) || txns.ValueKind != JsonValueKind.Array) return DecProp(refund, "amount");
        decimal sum = 0; var any = false;
        foreach (var t in txns.EnumerateArray())
            if (DecProp(t, "amount") is { } a) { sum += a; any = true; }
        return any ? sum : DecProp(refund, "amount");
    }

    private static SubmitResult Unauthorized() =>
        new(401, new { code = "invalid_credential", message = "Invalid connector or signature." });

    private static LoyaltyException Invalid(string msg) => new(LoyaltyError.ValidationFailed(msg));
}
