using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Campaigns.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Campaigns.Application;

public sealed record CreateCampaignInput(
    string Name, string? NameAr, string TriggerEventType, int MinimumEventVersion,
    DateTimeOffset? StartsAtUtc, DateTimeOffset? EndsAtUtc, int Priority, StackingPolicy StackingPolicy,
    IReadOnlyList<CampaignCondition> Conditions, IReadOnlyList<CampaignEffect> Effects);

public sealed record CampaignView(
    Guid Id, Guid ApplicationId, string Name, string? NameAr, string TriggerEventType, int MinimumEventVersion,
    int Priority, string StackingPolicy, string Status, int Version,
    IReadOnlyList<CampaignCondition> Conditions, IReadOnlyList<CampaignEffect> Effects);

public sealed class CampaignService(LoyaltyDbContext db, ApplicationContext scope, IClock clock)
{
    public async Task<CampaignView> CreateAsync(CreateCampaignInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) throw new LoyaltyException(LoyaltyError.ValidationFailed("name is required."));
        if (string.IsNullOrWhiteSpace(input.TriggerEventType)) throw new LoyaltyException(LoyaltyError.ValidationFailed("triggerEventType is required."));
        if (input.Effects.Count == 0) throw new LoyaltyException(LoyaltyError.ValidationFailed("at least one effect is required."));

        var c = Campaign.Create(Guid.NewGuid(), scope.ApplicationId, input.Name, input.NameAr, input.TriggerEventType,
            input.MinimumEventVersion <= 0 ? 1 : input.MinimumEventVersion, input.StartsAtUtc, input.EndsAtUtc,
            input.Priority, input.StackingPolicy, input.Conditions, input.Effects, clock.UtcNow);
        db.Campaigns.Add(c);
        await db.SaveChangesAsync(ct);
        return ToView(c);
    }

    public async Task<CampaignView?> ActivateAsync(Guid id, CancellationToken ct)
    {
        var c = await db.Campaigns.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return null;
        c.Activate(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return ToView(c);
    }

    public async Task<CampaignView?> PauseAsync(Guid id, CancellationToken ct)
    {
        var c = await db.Campaigns.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return null;
        c.Pause(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return ToView(c);
    }

    public async Task<CampaignView?> GetAsync(Guid id, CancellationToken ct)
    {
        var c = await db.Campaigns.FirstOrDefaultAsync(x => x.Id == id, ct);
        return c is null ? null : ToView(c);
    }

    public async Task<IReadOnlyList<CampaignView>> ListAsync(CancellationToken ct) =>
        (await db.Campaigns.OrderByDescending(c => c.Priority).ToListAsync(ct)).Select(ToView).ToList();

    private static CampaignView ToView(Campaign c) =>
        new(c.Id, c.ApplicationId, c.Name, c.NameAr, c.TriggerEventType, c.MinimumEventVersion, c.Priority,
            c.StackingPolicy.ToString(), c.Status.ToString(), c.Version, c.Conditions, c.Effects);
}
