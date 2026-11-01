using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Rewards.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Rewards.Application;

public sealed record CreateRewardInput(string Name, string? NameAr, string? Description, long PointsCost, RewardType RewardType, decimal? Value, int? Inventory, int? PerMemberLimit);
public sealed record UpdateRewardInput(string Name, string? NameAr, string? Description, long PointsCost, RewardType RewardType, decimal? Value, int? Inventory, int? PerMemberLimit, bool IsActive);
public sealed record RewardView(Guid Id, string Name, string? NameAr, string? Description, long PointsCost, string RewardType, decimal? Value, int? Inventory, int? PerMemberLimit, bool IsActive);

/// <summary>Manages the application-scoped reward catalog.</summary>
public sealed class RewardService(IRepository<Loyalty.Core.Modules.Rewards.Domain.Reward> rewardsRepo, IUnitOfWork uow, ApplicationContext scope, IClock clock)
{
    public async Task<RewardView> CreateAsync(CreateRewardInput input, CancellationToken ct)
    {
        var r = Reward.Create(scope.ApplicationId, input.Name, input.NameAr, input.Description, input.PointsCost,
            input.RewardType, input.Value, input.Inventory, input.PerMemberLimit, clock.UtcNow);
        rewardsRepo.Add(r);
        await uow.SaveChangesAsync(ct);
        return ToView(r);
    }

    public async Task<RewardView?> UpdateAsync(Guid id, UpdateRewardInput input, CancellationToken ct)
    {
        var r = await rewardsRepo.Query().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return null;
        r.Update(input.Name, input.NameAr, input.Description, input.PointsCost, input.RewardType, input.Value, input.Inventory, input.PerMemberLimit, input.IsActive, clock.UtcNow);
        await uow.SaveChangesAsync(ct);
        return ToView(r);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var r = await rewardsRepo.Query().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return false;
        rewardsRepo.Remove(r);
        await uow.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<RewardView>> ListAsync(bool activeOnly, CancellationToken ct)
    {
        var q = rewardsRepo.Query().AsQueryable();
        if (activeOnly) q = q.Where(r => r.IsActive);
        return (await q.OrderBy(r => r.PointsCost).ToListAsync(ct)).Select(ToView).ToList();
    }

    public async Task<RewardView?> GetAsync(Guid id, CancellationToken ct)
    {
        var r = await rewardsRepo.Query().FirstOrDefaultAsync(x => x.Id == id, ct);
        return r is null ? null : ToView(r);
    }

    private static RewardView ToView(Reward r) =>
        new(r.Id, r.Name, r.NameAr, r.Description, r.PointsCost, r.RewardType.ToString(), r.Value, r.Inventory, r.PerMemberLimit, r.IsActive);
}
