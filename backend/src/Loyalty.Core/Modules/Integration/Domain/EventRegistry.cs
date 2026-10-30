using System.Text.Json;
using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Integration.Contracts;

namespace Loyalty.Core.Modules.Integration.Domain;

/// <summary>Normalized, validated typed attributes for a known event + its business key.</summary>
public sealed record NormalizedEvent(
    string EventType,
    int EventVersion,
    string BusinessEventKey,
    decimal? Amount,
    string? Currency,
    long? SignedPoints,
    IReadOnlyDictionary<string, object?> Facts);

/// <summary>
/// A code-owned registry that recognizes event types and versions, validates attributes,
/// normalizes them, and builds a deterministic business-event key per type (MVP spec §8, §9).
/// Database-configurable schemas are deferred.
/// </summary>
public sealed class EventRegistry
{
    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        EventTypes.PurchaseCompleted, EventTypes.OrderRefunded, EventTypes.CustomerRegistered,
        EventTypes.ApplicationLogin, EventTypes.ReferralCompleted, EventTypes.ManualAdjustmentRequested,
    };

    public bool IsKnown(string eventType, int eventVersion) => eventVersion == 1 && Known.Contains(eventType);

    public NormalizedEvent Normalize(LoyaltyEventEnvelopeV1 e, string sourceSystem)
    {
        if (!IsKnown(e.EventType, e.EventVersion))
            throw new LoyaltyException(LoyaltyError.UnsupportedEventContract($"Unsupported event contract: {e.EventType} v{e.EventVersion}."));

        var attrs = e.Attributes;
        if (attrs.ValueKind != JsonValueKind.Object)
            throw Invalid("attributes must be an object.");

        var facts = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["customerId"] = e.CustomerId,
            ["occurredAt"] = e.OccurredAt,
        };
        foreach (var prop in attrs.EnumerateObject())
            facts[$"attributes.{prop.Name}"] = ScalarOf(prop.Value);

        return e.EventType switch
        {
            EventTypes.PurchaseCompleted => Purchase(e, attrs, sourceSystem, facts),
            EventTypes.OrderRefunded => Refund(e, attrs, sourceSystem, facts),
            EventTypes.CustomerRegistered => Register(e, sourceSystem, facts),
            EventTypes.ApplicationLogin => Login(e, attrs, sourceSystem, facts),
            EventTypes.ReferralCompleted => Referral(e, attrs, sourceSystem, facts),
            EventTypes.ManualAdjustmentRequested => ManualAdjustment(e, attrs, sourceSystem, facts),
            _ => throw new LoyaltyException(LoyaltyError.UnsupportedEventContract($"Unsupported event contract: {e.EventType}.")),
        };
    }

    private static NormalizedEvent Purchase(LoyaltyEventEnvelopeV1 e, JsonElement a, string src, Dictionary<string, object?> facts)
    {
        var orderId = Str(a, "orderId") ?? throw Invalid("orderId is required.");
        var amount = Dec(a, "amount") ?? throw Invalid("amount is required and must be a number.");
        if (amount < 0) throw Invalid("amount must be non-negative.");
        var currency = Str(a, "currency") ?? throw Invalid("currency is required.");
        facts["amount"] = amount; facts["currency"] = currency.ToUpperInvariant();
        return new(e.EventType, 1, $"{src}|{orderId}", amount, currency.ToUpperInvariant(), null, facts);
    }

    private static NormalizedEvent Refund(LoyaltyEventEnvelopeV1 e, JsonElement a, string src, Dictionary<string, object?> facts)
    {
        var refundId = Str(a, "refundId") ?? throw Invalid("refundId is required.");
        _ = Str(a, "originalOrderId") ?? throw Invalid("originalOrderId is required.");
        var amount = Dec(a, "amount") ?? throw Invalid("amount is required and must be a number.");
        if (amount < 0) throw Invalid("amount must be non-negative.");
        var currency = Str(a, "currency") ?? throw Invalid("currency is required.");
        facts["amount"] = amount; facts["currency"] = currency.ToUpperInvariant();
        return new(e.EventType, 1, $"{src}|{refundId}", amount, currency.ToUpperInvariant(), null, facts);
    }

    private static NormalizedEvent Register(LoyaltyEventEnvelopeV1 e, string src, Dictionary<string, object?> facts)
        => new(e.EventType, 1, $"{src}|{e.CustomerId}", null, null, null, facts);

    private static NormalizedEvent Login(LoyaltyEventEnvelopeV1 e, JsonElement a, string src, Dictionary<string, object?> facts)
    {
        _ = Str(a, "channel") ?? throw Invalid("channel is required.");
        var rewardDate = e.OccurredAt.UtcDateTime.ToString("yyyy-MM-dd");
        return new(e.EventType, 1, $"{src}|{e.CustomerId}|{rewardDate}", null, null, null, facts);
    }

    private static NormalizedEvent Referral(LoyaltyEventEnvelopeV1 e, JsonElement a, string src, Dictionary<string, object?> facts)
    {
        var referralId = Str(a, "referralId") ?? throw Invalid("referralId is required.");
        return new(e.EventType, 1, $"{src}|{referralId}", null, null, null, facts);
    }

    private static NormalizedEvent ManualAdjustment(LoyaltyEventEnvelopeV1 e, JsonElement a, string src, Dictionary<string, object?> facts)
    {
        var adjustmentId = Str(a, "adjustmentId") ?? throw Invalid("adjustmentId is required.");
        var signed = Long(a, "signedPoints") ?? throw Invalid("signedPoints is required and must be an integer.");
        if (signed == 0) throw Invalid("signedPoints must be non-zero.");
        _ = Str(a, "reason") ?? throw Invalid("reason is required.");
        facts["signedPoints"] = signed;
        return new(e.EventType, 1, $"{src}|{adjustmentId}", null, null, signed, facts);
    }

    private static LoyaltyException Invalid(string msg) => new(LoyaltyError.ValidationFailed(msg));
    private static string? Str(JsonElement o, string n) => o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static decimal? Dec(JsonElement o, string n) => o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : null;
    private static long? Long(JsonElement o, string n) => o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : null;

    private static object? ScalarOf(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.TryGetDecimal(out var d) ? d : null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}
