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
    IReadOnlyDictionary<string, object?> Facts);

/// <summary>
/// A code-owned registry that recognizes event types and versions, validates attributes,
/// normalizes them, and builds a deterministic business-event key per type (MVP spec §8, §9).
/// Database-configurable schemas are deferred. Phase 1 registers PurchaseCompleted v1.
/// </summary>
public sealed class EventRegistry
{
    public bool IsKnown(string eventType, int eventVersion) =>
        eventType == EventTypes.PurchaseCompleted && eventVersion == 1;

    /// <summary>
    /// Validate + normalize. Throws <see cref="LoyaltyException"/> (422) on invalid attributes,
    /// or (400) on an unsupported contract.
    /// </summary>
    public NormalizedEvent Normalize(LoyaltyEventEnvelopeV1 e, string sourceSystem)
    {
        if (!IsKnown(e.EventType, e.EventVersion))
            throw new LoyaltyException(LoyaltyError.UnsupportedEventContract(
                $"Unsupported event contract: {e.EventType} v{e.EventVersion}."));

        return e.EventType switch
        {
            EventTypes.PurchaseCompleted => NormalizePurchase(e, sourceSystem),
            _ => throw new LoyaltyException(LoyaltyError.UnsupportedEventContract(
                $"Unsupported event contract: {e.EventType} v{e.EventVersion}.")),
        };
    }

    // PurchaseCompleted → business key = SourceSystem + OrderId (§9.3)
    private static NormalizedEvent NormalizePurchase(LoyaltyEventEnvelopeV1 e, string sourceSystem)
    {
        var attrs = e.Attributes;
        if (attrs.ValueKind != JsonValueKind.Object)
            throw Invalid("attributes must be an object.");

        var orderId = GetString(attrs, "orderId") ?? throw Invalid("orderId is required.");
        var amount = GetDecimal(attrs, "amount") ?? throw Invalid("amount is required and must be a number.");
        if (amount < 0) throw Invalid("amount must be non-negative.");
        var currency = GetString(attrs, "currency") ?? throw Invalid("currency is required.");
        var category = GetString(attrs, "category");

        var facts = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["amount"] = amount,
            ["currency"] = currency.ToUpperInvariant(),
            ["category"] = category,
            ["orderId"] = orderId,
            ["customerId"] = e.CustomerId,
            ["occurredAt"] = e.OccurredAt,
        };
        // expose every scalar attribute under attributes.<name> for condition fields
        foreach (var prop in attrs.EnumerateObject())
            facts[$"attributes.{prop.Name}"] = ScalarOf(prop.Value);

        var businessKey = $"{sourceSystem}|{orderId}";
        return new NormalizedEvent(e.EventType, e.EventVersion, businessKey, amount, currency.ToUpperInvariant(), facts);
    }

    private static LoyaltyException Invalid(string msg) =>
        new(LoyaltyError.ValidationFailed(msg));

    private static string? GetString(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static decimal? GetDecimal(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : null;

    private static object? ScalarOf(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.TryGetDecimal(out var d) ? d : null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}
