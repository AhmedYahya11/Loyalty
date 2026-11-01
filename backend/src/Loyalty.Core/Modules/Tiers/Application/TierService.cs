using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Tiers.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Tiers.Application;

public sealed record CreateTierInput(string Name, string? NameAr, int Rank, TierMetric ThresholdMetric, decimal ThresholdValue, decimal EarnMultiplier, string? Color);
public sealed record UpdateTierInput(string Name, string? NameAr, decimal ThresholdValue, decimal EarnMultiplier, string? Color, bool IsActive);
public sealed record TierView(Guid Id, string Name, string? NameAr, int Rank, string ThresholdMetric, decimal ThresholdValue, decimal EarnMultiplier, string? Color, bool IsActive);

/// <summary>Manages application-scoped membership tiers (MVP spec future module "Tiers").</summary>
public sealed class TierService(IRepository<Loyalty.Core.Modules.Tiers.Domain.Tier> tiersRepo, IUnitOfWork uow, ApplicationContext scope, IClock clock)
{
    public async Task<TierView> CreateAsync(CreateTierInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) throw new LoyaltyException(LoyaltyError.ValidationFailed("name is required."));
        if (await tiersRepo.Query().AnyAsync(t => t.Rank == input.Rank, ct))
            throw new LoyaltyException(LoyaltyError.Conflict("duplicate_tier_rank", $"A tier with rank {input.Rank} already exists."));

        var tier = Tier.Create(scope.ApplicationId, input.Name, input.NameAr, input.Rank, input.ThresholdMetric,
            input.ThresholdValue, input.EarnMultiplier, input.Color, clock.UtcNow);
        tiersRepo.Add(tier);
        await uow.SaveChangesAsync(ct);
        return ToView(tier);
    }

    public async Task<TierView?> UpdateAsync(Guid id, UpdateTierInput input, CancellationToken ct)
    {
        var tier = await tiersRepo.Query().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tier is null) return null;
        tier.Update(input.Name, input.NameAr, input.ThresholdValue, input.EarnMultiplier, input.Color, input.IsActive, clock.UtcNow);
        await uow.SaveChangesAsync(ct);
        return ToView(tier);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var tier = await tiersRepo.Query().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tier is null) return false;
        tiersRepo.Remove(tier);
        await uow.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<TierView>> ListAsync(CancellationToken ct) =>
        (await tiersRepo.Query().OrderBy(t => t.Rank).ToListAsync(ct)).Select(ToView).ToList();

    public async Task<TierView?> GetAsync(Guid id, CancellationToken ct)
    {
        var t = await tiersRepo.Query().FirstOrDefaultAsync(x => x.Id == id, ct);
        return t is null ? null : ToView(t);
    }

    private static TierView ToView(Tier t) =>
        new(t.Id, t.Name, t.NameAr, t.Rank, t.ThresholdMetric.ToString(), t.ThresholdValue, t.EarnMultiplier, t.Color, t.IsActive);
}
