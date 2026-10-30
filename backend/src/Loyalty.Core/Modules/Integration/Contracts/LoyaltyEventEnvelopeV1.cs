using System.Text.Json;

namespace Loyalty.Core.Modules.Integration.Contracts;

/// <summary>
/// The single public event envelope accepted by POST /api/v1/events (MVP spec §8).
/// ApplicationId, AccountId, and SourceSystem are NEVER trusted from the body — they are
/// derived from the authenticated account.
/// </summary>
public sealed record LoyaltyEventEnvelopeV1(
    Guid EventId,
    string EventType,
    int EventVersion,
    string SourceTransactionId,
    string CustomerId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    JsonElement Attributes);

/// <summary>Known event types for the MVP registry (§8). Phase 1 implements PurchaseCompleted.</summary>
public static class EventTypes
{
    public const string PurchaseCompleted = "PurchaseCompleted";
    public const string OrderRefunded = "OrderRefunded";
    public const string CustomerRegistered = "CustomerRegistered";
    public const string ApplicationLogin = "ApplicationLogin";
    public const string ReferralCompleted = "ReferralCompleted";
    public const string ManualAdjustmentRequested = "ManualAdjustmentRequested";
    /// <summary>A generic, tenant-defined user action (e.g. app.shared, app.installed, review.left),
    /// carried in attributes.action — lets campaigns/stamp cards/raffles reward any action (P15).</summary>
    public const string CustomAction = "CustomAction";
}
