using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Gamification.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Gamification.Application;

public sealed record CreateAchievementInput(string Name, string? NameAr, string? Description, string? Icon, GamificationMetric Metric, decimal Threshold, long BonusPoints);
public sealed record UpdateAchievementInput(string Name, string? NameAr, string? Description, string? Icon, decimal Threshold, long BonusPoints, bool IsActive);
public sealed record AchievementView(Guid Id, string Name, string? NameAr, string? Description, string? Icon, string Metric, decimal Threshold, long BonusPoints, bool IsActive);

public sealed class AchievementService(LoyaltyDbContext db, ApplicationContext scope, IClock clock)
{
    public async Task<AchievementView> CreateAsync(CreateAchievementInput i, CancellationToken ct)
    {
        var a = Achievement.Create(scope.ApplicationId, i.Name, i.NameAr, i.Description, i.Icon, i.Metric, i.Threshold, i.BonusPoints, clock.UtcNow);
        db.Achievements.Add(a);
        await db.SaveChangesAsync(ct);
        return ToView(a);
    }

    public async Task<AchievementView?> UpdateAsync(Guid id, UpdateAchievementInput i, CancellationToken ct)
    {
        var a = await db.Achievements.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return null;
        a.Update(i.Name, i.NameAr, i.Description, i.Icon, i.Threshold, i.BonusPoints, i.IsActive, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return ToView(a);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var a = await db.Achievements.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return false;
        db.Achievements.Remove(a);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<AchievementView>> ListAsync(CancellationToken ct) =>
        (await db.Achievements.OrderBy(a => a.Threshold).ToListAsync(ct)).Select(ToView).ToList();

    private static AchievementView ToView(Achievement a) =>
        new(a.Id, a.Name, a.NameAr, a.Description, a.Icon, a.Metric.ToString(), a.Threshold, a.BonusPoints, a.IsActive);
}
