using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Tiers.Domain;

/// <summary>The member metric a tier threshold is measured against.</summary>
public enum TierMetric { NetSpend = 0, PointsEarned = 1, PurchaseCount = 2 }

/// <summary>
/// An application-scoped membership tier (e.g. Bronze/Silver/Gold/Platinum). Members progress to
/// the highest tier whose threshold they meet; the tier grants an earn-multiplier benefit applied
/// to base points. Rank 0 is the base tier (threshold 0 → everyone qualifies).
/// </summary>
public sealed class Tier : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? NameAr { get; private set; }
    public int Rank { get; private set; }
    public TierMetric ThresholdMetric { get; private set; }
    public decimal ThresholdValue { get; private set; }
    public decimal EarnMultiplier { get; private set; }
    public string? Color { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Tier() { }

    public static Tier Create(
        Guid applicationId, string name, string? nameAr, int rank, TierMetric metric, decimal thresholdValue,
        decimal earnMultiplier, string? color, DateTimeOffset now)
    {
        Validate(rank, thresholdValue, earnMultiplier);
        return new Tier
        {
            Id = Guid.NewGuid(),
            ApplicationId = applicationId,
            Name = name.Trim(),
            NameAr = string.IsNullOrWhiteSpace(nameAr) ? null : nameAr.Trim(),
            Rank = rank,
            ThresholdMetric = metric,
            ThresholdValue = thresholdValue,
            EarnMultiplier = earnMultiplier,
            Color = color,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(string name, string? nameAr, decimal thresholdValue, decimal earnMultiplier, string? color, bool isActive, DateTimeOffset now)
    {
        Validate(Rank, thresholdValue, earnMultiplier);
        Name = name.Trim();
        NameAr = string.IsNullOrWhiteSpace(nameAr) ? null : nameAr.Trim();
        ThresholdValue = thresholdValue;
        EarnMultiplier = earnMultiplier;
        Color = color;
        IsActive = isActive;
        UpdatedAt = now;
    }

    public bool Meets(decimal netSpend, long pointsEarned, long purchaseCount) => ThresholdMetric switch
    {
        TierMetric.NetSpend => netSpend >= ThresholdValue,
        TierMetric.PointsEarned => pointsEarned >= ThresholdValue,
        TierMetric.PurchaseCount => purchaseCount >= ThresholdValue,
        _ => false,
    };

    private static void Validate(int rank, decimal threshold, decimal multiplier)
    {
        if (rank < 0) throw new LoyaltyException(LoyaltyError.ValidationFailed("rank must be >= 0."));
        if (threshold < 0) throw new LoyaltyException(LoyaltyError.ValidationFailed("thresholdValue must be >= 0."));
        if (multiplier <= 0) throw new LoyaltyException(LoyaltyError.ValidationFailed("earnMultiplier must be > 0."));
    }
}
