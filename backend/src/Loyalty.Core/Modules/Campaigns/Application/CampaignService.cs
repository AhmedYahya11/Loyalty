using System.Text.Json;
using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Campaigns.Domain;
using Loyalty.Core.Modules.Integration.Contracts;
using Loyalty.Core.Modules.Integration.Domain;
using Loyalty.Core.Modules.Settings.Application;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Campaigns.Application;

public sealed record CreateCampaignInput(
    string Name, string? NameAr, string TriggerEventType, int MinimumEventVersion,
    DateTimeOffset? StartsAtUtc, DateTimeOffset? EndsAtUtc, int Priority, StackingPolicy StackingPolicy,
    IReadOnlyList<CampaignCondition> Conditions, IReadOnlyList<CampaignEffect> Effects,
    IReadOnlyList<string>? RequiredLifecycleStages = null,
    IReadOnlyList<Guid>? RequiredSegmentIds = null,
    IReadOnlyList<Guid>? ExcludedSegmentIds = null,
    IReadOnlyList<MemberLimit>? MemberLimits = null,
    long? GlobalBudget = null);

public sealed record CampaignView(
    Guid Id, Guid ApplicationId, string Name, string? NameAr, string TriggerEventType, int MinimumEventVersion,
    int Priority, string StackingPolicy, string Status, int Version,
    IReadOnlyList<CampaignCondition> Conditions, IReadOnlyList<CampaignEffect> Effects,
    IReadOnlyList<string> RequiredLifecycleStages, IReadOnlyList<Guid> RequiredSegmentIds, IReadOnlyList<Guid> ExcludedSegmentIds,
    IReadOnlyList<MemberLimit> MemberLimits, long? GlobalBudgetInitial, long? GlobalBudgetRemaining);

public sealed record SimulateInput(string EventType, int EventVersion, string CustomerId, DateTimeOffset OccurredAt, JsonElement Attributes);

public sealed class CampaignService(
    IRepository<Loyalty.Core.Modules.Rewards.Domain.Reward> rewardsRepo,
    IRepository<Loyalty.Core.Modules.Campaigns.Domain.Campaign> campaignsRepo,
    IRepository<Loyalty.Core.Modules.Cvm.Domain.SegmentDefinition> segmentsRepo,
    IRepository<Loyalty.Core.Modules.Members.Domain.Member> membersRepo,
    IRepository<Loyalty.Core.Modules.Cvm.Domain.MemberValueProfile> valueProfilesRepo,
    IRepository<Loyalty.Core.Modules.Cvm.Domain.MemberSegmentMembership> segmentMembershipsRepo,
    IUnitOfWork uow, ApplicationContext scope, IClock clock,
    EventRegistry registry, LoyaltySettingsService settings, RewardDecisionService decider)
{
    public async Task<CampaignView> CreateAsync(CreateCampaignInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) throw new LoyaltyException(LoyaltyError.ValidationFailed("name is required."));
        if (string.IsNullOrWhiteSpace(input.TriggerEventType)) throw new LoyaltyException(LoyaltyError.ValidationFailed("triggerEventType is required."));
        if (input.Effects.Count == 0) throw new LoyaltyException(LoyaltyError.ValidationFailed("at least one effect is required."));
        if (input.GlobalBudget is < 0) throw new LoyaltyException(LoyaltyError.ValidationFailed("globalBudget must be non-negative."));

        // GrantReward effects must reference an existing reward (P16)
        foreach (var e in input.Effects.Where(e => e.Type == EffectType.GrantReward))
        {
            if (e.RewardId is null) throw new LoyaltyException(LoyaltyError.ValidationFailed("GrantReward effect requires a rewardId."));
            if (!await rewardsRepo.Query().AnyAsync(r => r.Id == e.RewardId, ct)) throw new LoyaltyException(LoyaltyError.NotFound("Reward"));
        }

        var c = Campaign.Create(Guid.NewGuid(), scope.ApplicationId, input.Name, input.NameAr, input.TriggerEventType,
            input.MinimumEventVersion <= 0 ? 1 : input.MinimumEventVersion, input.StartsAtUtc, input.EndsAtUtc,
            input.Priority, input.StackingPolicy, input.Conditions, input.Effects,
            input.RequiredLifecycleStages, input.RequiredSegmentIds, input.ExcludedSegmentIds,
            input.MemberLimits, input.GlobalBudget, clock.UtcNow);
        campaignsRepo.Add(c);
        await uow.SaveChangesAsync(ct);
        return ToView(c);
    }

    public async Task<CampaignView?> ValidateAsync(Guid id, CancellationToken ct)
    {
        var c = await campaignsRepo.Query().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return null;

        // targeting segments must exist and be active
        var referenced = c.RequiredSegmentIds.Concat(c.ExcludedSegmentIds).Distinct().ToList();
        if (referenced.Count > 0)
        {
            var activeCount = await segmentsRepo.Query().CountAsync(s => referenced.Contains(s.Id) && s.Status == Cvm.Domain.SegmentStatus.Active, ct);
            if (activeCount != referenced.Count)
                throw new LoyaltyException(LoyaltyError.Conflict("CVM_NOT_CONFIGURED", "A targeted segment is missing or not active."));
        }

        c.Validate(clock.UtcNow);
        await uow.SaveChangesAsync(ct);
        return ToView(c);
    }

    public async Task<CampaignView?> ActivateAsync(Guid id, CancellationToken ct)
    {
        var c = await campaignsRepo.Query().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return null;
        try { c.Activate(clock.UtcNow); }
        catch (InvalidOperationException ex) { throw new LoyaltyException(LoyaltyError.Conflict("campaign_not_validated", ex.Message)); }
        await uow.SaveChangesAsync(ct);
        return ToView(c);
    }

    public async Task<CampaignView?> PauseAsync(Guid id, CancellationToken ct)
    {
        var c = await campaignsRepo.Query().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return null;
        c.Pause(clock.UtcNow);
        await uow.SaveChangesAsync(ct);
        return ToView(c);
    }

    public async Task<object?> SimulateAsync(Guid id, SimulateInput input, CancellationToken ct)
    {
        var c = await campaignsRepo.Query().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return null;

        var effective = await settings.ResolveEffectiveAsync(scope.ApplicationId, ct)
            ?? throw new LoyaltyException(LoyaltyError.LoyaltyNotConfigured());

        var envelope = new LoyaltyEventEnvelopeV1(Guid.NewGuid(), input.EventType, input.EventVersion,
            "sim", input.CustomerId, input.OccurredAt, "sim", input.Attributes);
        var normalized = registry.Normalize(envelope, scope.SourceSystem);

        // resolve the simulated member's lifecycle + active segments (default New if unknown)
        var member = await membersRepo.Query().FirstOrDefaultAsync(m => m.ExternalCustomerId == input.CustomerId, ct);
        var lifecycle = "New";
        var activeSegments = new HashSet<Guid>();
        if (member is not null)
        {
            var profile = await valueProfilesRepo.Query().FirstOrDefaultAsync(p => p.MemberId == member.Id, ct);
            if (profile is not null) lifecycle = profile.LifecycleStage.ToString();
            activeSegments = (await segmentMembershipsRepo.Query().Where(m => m.MemberId == member.Id && m.IsActive)
                .Select(m => m.SegmentId).ToListAsync(ct)).ToHashSet();
        }

        var eligible = decider.SelectEligible(normalized, [c], lifecycle, activeSegments);
        var isEligible = eligible.Count > 0;
        var effects = decider.ComputeStackedEffects(effective.SpendAmountPerPoint, effective.RoundingPolicy, normalized, eligible);

        return new
        {
            eligible = isEligible,
            lifecycleStage = lifecycle,
            matchedSegments = activeSegments,
            effects = effects.Effects.Select(e => new { e.EffectId, e.Points }),
            changesPersisted = false,
        };
    }

    public async Task<CampaignView?> GetAsync(Guid id, CancellationToken ct)
    {
        var c = await campaignsRepo.Query().FirstOrDefaultAsync(x => x.Id == id, ct);
        return c is null ? null : ToView(c);
    }

    public async Task<IReadOnlyList<CampaignView>> ListAsync(CancellationToken ct) =>
        (await campaignsRepo.Query().OrderByDescending(c => c.Priority).ToListAsync(ct)).Select(ToView).ToList();

    private static CampaignView ToView(Campaign c) =>
        new(c.Id, c.ApplicationId, c.Name, c.NameAr, c.TriggerEventType, c.MinimumEventVersion, c.Priority,
            c.StackingPolicy.ToString(), c.Status.ToString(), c.Version, c.Conditions, c.Effects,
            c.RequiredLifecycleStages, c.RequiredSegmentIds, c.ExcludedSegmentIds,
            c.MemberLimits, c.GlobalBudgetInitial, c.GlobalBudgetRemaining);
}
