using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Rewards.Domain;

public enum RewardType { Coupon = 0, Voucher = 1, Discount = 2, FreeItem = 3, Other = 4 }

/// <summary>
/// A catalog item a member can redeem points for (MVP spec future "Reward catalog" module).
/// Inventory null = unlimited; PerMemberLimit null = unlimited.
/// </summary>
public sealed class Reward : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? NameAr { get; private set; }
    public string? Description { get; private set; }
    public long PointsCost { get; private set; }
    public RewardType RewardType { get; private set; }
    public decimal? Value { get; private set; }
    public int? Inventory { get; private set; }
    public int? PerMemberLimit { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Reward() { }

    public static Reward Create(
        Guid applicationId, string name, string? nameAr, string? description, long pointsCost, RewardType type,
        decimal? value, int? inventory, int? perMemberLimit, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new LoyaltyException(LoyaltyError.ValidationFailed("name is required."));
        if (pointsCost <= 0) throw new LoyaltyException(LoyaltyError.ValidationFailed("pointsCost must be greater than zero."));
        if (inventory is < 0) throw new LoyaltyException(LoyaltyError.ValidationFailed("inventory must be >= 0."));
        return new Reward
        {
            Id = Guid.NewGuid(),
            ApplicationId = applicationId,
            Name = name.Trim(),
            NameAr = string.IsNullOrWhiteSpace(nameAr) ? null : nameAr.Trim(),
            Description = description,
            PointsCost = pointsCost,
            RewardType = type,
            Value = value,
            Inventory = inventory,
            PerMemberLimit = perMemberLimit,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(string name, string? nameAr, string? description, long pointsCost, RewardType type,
        decimal? value, int? inventory, int? perMemberLimit, bool isActive, DateTimeOffset now)
    {
        if (pointsCost <= 0) throw new LoyaltyException(LoyaltyError.ValidationFailed("pointsCost must be greater than zero."));
        Name = name.Trim();
        NameAr = string.IsNullOrWhiteSpace(nameAr) ? null : nameAr.Trim();
        Description = description;
        PointsCost = pointsCost;
        RewardType = type;
        Value = value;
        Inventory = inventory;
        PerMemberLimit = perMemberLimit;
        IsActive = isActive;
        UpdatedAt = now;
    }

    /// <summary>Hold one unit of inventory (no-op when unlimited). Returns false when sold out.</summary>
    public bool TryHoldInventory()
    {
        if (Inventory is null) return true;
        if (Inventory <= 0) return false;
        Inventory--;
        return true;
    }

    public void RestoreInventory()
    {
        if (Inventory is not null) Inventory++;
    }
}
