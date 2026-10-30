using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Settings.Domain;

public enum RoundingPolicy { Floor = 0 } // MVP supports Floor only (§10)

public enum LoyaltyStatus { Draft = 0, Active = 1, Paused = 2, Closed = 3 }

/// <summary>
/// Optional one-to-one reward-calculation configuration for an application. Resolved with
/// whole-row nearest-ancestor inheritance (a child row is a complete override, never a
/// field-level merge). PK is ApplicationId (MVP spec §10).
/// </summary>
public sealed class ApplicationLoyaltySettings : IApplicationScoped
{
    public Guid ApplicationId { get; private set; }
    public int Version { get; private set; }
    public string PointsUnitCode { get; private set; } = default!;
    public string BaseCurrency { get; private set; } = default!;
    public decimal SpendAmountPerPoint { get; private set; }
    public RoundingPolicy RoundingPolicy { get; private set; }
    public LoyaltyStatus LoyaltyStatus { get; private set; }
    public DateTimeOffset? LoyaltyStartsAtUtc { get; private set; }
    public DateTimeOffset? LoyaltyEndsAtUtc { get; private set; }
    /// <summary>Points expire this many days after being earned. 0 = never expire (P13).</summary>
    public int PointsExpiryDays { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ApplicationLoyaltySettings() { }

    public static ApplicationLoyaltySettings Create(
        Guid applicationId, string pointsUnitCode, string baseCurrency, decimal spendAmountPerPoint,
        RoundingPolicy rounding, LoyaltyStatus status,
        DateTimeOffset? startsAt, DateTimeOffset? endsAt, DateTimeOffset now, int pointsExpiryDays = 0) => new()
    {
        ApplicationId = applicationId,
        Version = 1,
        PointsUnitCode = pointsUnitCode.Trim().ToUpperInvariant(),
        BaseCurrency = baseCurrency.Trim().ToUpperInvariant(),
        SpendAmountPerPoint = spendAmountPerPoint,
        RoundingPolicy = rounding,
        LoyaltyStatus = status,
        LoyaltyStartsAtUtc = startsAt,
        LoyaltyEndsAtUtc = endsAt,
        PointsExpiryDays = pointsExpiryDays < 0 ? 0 : pointsExpiryDays,
        CreatedAt = now,
        UpdatedAt = now,
    };

    /// <summary>Replace the whole row (a new settings version) keeping the same unit.</summary>
    public void Replace(
        string pointsUnitCode, string baseCurrency, decimal spendAmountPerPoint,
        RoundingPolicy rounding, LoyaltyStatus status,
        DateTimeOffset? startsAt, DateTimeOffset? endsAt, DateTimeOffset now, int pointsExpiryDays = 0)
    {
        Version += 1;
        PointsUnitCode = pointsUnitCode.Trim().ToUpperInvariant();
        BaseCurrency = baseCurrency.Trim().ToUpperInvariant();
        SpendAmountPerPoint = spendAmountPerPoint;
        RoundingPolicy = rounding;
        LoyaltyStatus = status;
        LoyaltyStartsAtUtc = startsAt;
        LoyaltyEndsAtUtc = endsAt;
        PointsExpiryDays = pointsExpiryDays < 0 ? 0 : pointsExpiryDays;
        UpdatedAt = now;
    }

    /// <summary>Active and within the optional eligibility window for the given event time.</summary>
    public bool IsActiveAt(DateTimeOffset occurredAt) =>
        LoyaltyStatus == LoyaltyStatus.Active
        && (LoyaltyStartsAtUtc is null || occurredAt >= LoyaltyStartsAtUtc)
        && (LoyaltyEndsAtUtc is null || occurredAt <= LoyaltyEndsAtUtc);

    /// <summary>floor(amount / SpendAmountPerPoint) — base purchase calculation (§10).</summary>
    public long CalculateBasePoints(decimal amount) =>
        PointsMath.CalculateBasePoints(amount, SpendAmountPerPoint, RoundingPolicy);
}
