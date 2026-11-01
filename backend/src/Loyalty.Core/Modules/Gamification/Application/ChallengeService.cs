using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Gamification.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Gamification.Application;

public sealed record CreateChallengeInput(string Name, string? NameAr, string? Description, GamificationMetric Metric, decimal TargetValue, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, long BonusPoints);
public sealed record ChallengeView(Guid Id, string Name, string? NameAr, string? Description, string Metric, decimal TargetValue, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, long BonusPoints, bool IsActive);

public sealed class ChallengeService(IRepository<Loyalty.Core.Modules.Gamification.Domain.Challenge> challengesRepo, IUnitOfWork uow, ApplicationContext scope, IClock clock)
{
    public async Task<ChallengeView> CreateAsync(CreateChallengeInput i, CancellationToken ct)
    {
        var c = Challenge.Create(scope.ApplicationId, i.Name, i.NameAr, i.Description, i.Metric, i.TargetValue, i.StartsAtUtc, i.EndsAtUtc, i.BonusPoints, clock.UtcNow);
        challengesRepo.Add(c);
        await uow.SaveChangesAsync(ct);
        return ToView(c);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var c = await challengesRepo.Query().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return false;
        challengesRepo.Remove(c);
        await uow.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<ChallengeView>> ListAsync(CancellationToken ct) =>
        (await challengesRepo.Query().OrderByDescending(c => c.StartsAtUtc).ToListAsync(ct)).Select(ToView).ToList();

    private static ChallengeView ToView(Challenge c) =>
        new(c.Id, c.Name, c.NameAr, c.Description, c.Metric.ToString(), c.TargetValue, c.StartsAtUtc, c.EndsAtUtc, c.BonusPoints, c.IsActive);
}
