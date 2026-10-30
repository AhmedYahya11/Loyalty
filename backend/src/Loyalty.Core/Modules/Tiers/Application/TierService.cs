using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Tiers.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Tiers.Application;

public sealed record CreateTierInput(string Name, string? NameAr, int Rank, TierMetric ThresholdMetric, decimal ThresholdValue, decimal EarnMultiplier, string? Color);
public sealed record UpdateTierInput(string Name, string? NameAr, decimal ThresholdValue, decimal EarnMultiplier, string? Color, bool IsActive);
public sealed record TierView(Guid Id, string Name, string? NameAr, int Rank, string ThresholdMetric, decimal ThresholdValue, decimal EarnMultiplier, string? Color, bool IsActive);

/// <summary>Manages application-scoped membership tiers (MVP spec future module "Tiers").</summary>
public sealed class TierService(LoyaltyDbContext db, ApplicationContext scope, IClock clock)
{
    public async Task<TierView> CreateAsync(CreateTierInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) throw new LoyaltyException(LoyaltyError.ValidationFailed("name is required."));
        if (await db.Tiers.AnyAsync(t => t.Rank == input.Rank, ct))
            throw new LoyaltyException(LoyaltyError.Conflict("duplicate_tier_rank", $"A tier with rank {input.Rank} already exists."));

        var tier = Tier.Create(scope.ApplicationId, input.Name, input.NameAr, input.Rank, input.ThresholdMetric,
            input.ThresholdValue, input.EarnMultiplier, input.Color, clock.UtcNow);
        db.Tiers.Add(tier);
        await db.SaveChangesAsync(ct);
        return ToView(tier);
    }

    public async Task<TierView?> UpdateAsync(Guid id, UpdateTierInput input, CancellationToken ct)
    {
        var tier = await db.Tiers.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tier is null) return null;
        tier.Update(input.Name, input.NameAr, input.ThresholdValue, input.EarnMultiplier, input.Color, input.IsActive, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return ToView(tier);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var tier = await db.Tiers.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tier is null) return false;
        db.Tiers.Remove(tier);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<TierView>> ListAsync(CancellationToken ct) =>
        (await db.Tiers.OrderBy(t => t.Rank).ToListAsync(ct)).Select(ToView).ToList();

    public async Task<TierView?> GetAsync(Guid id, CancellationToken ct)
    {
        var t = await db.Tiers.FirstOrDefaultAsync(x => x.Id == id, ct);
        return t is null ? null : ToView(t);
    }

    private static TierView ToView(Tier t) =>
        new(t.Id, t.Name, t.NameAr, t.Rank, t.ThresholdMetric.ToString(), t.ThresholdValue, t.EarnMultiplier, t.Color, t.IsActive);
}
