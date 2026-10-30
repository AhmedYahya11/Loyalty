using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Campaigns.Domain;

/// <summary>
/// A reward campaign triggered by an event type. Conditions and effects are stored as
/// owned JSON. For the Phase-1 vertical slice this carries the fields needed to select,
/// evaluate, and emit effects; audience targeting (lifecycle/segments), budgets, and
/// member limits are layered in later phases (MVP spec §12).
/// </summary>
public sealed class Campaign : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public string Name { get; private set; } = default!;
    /// <summary>Optional Arabic campaign name (bilingual support). اسم الحملة بالعربية.</summary>
    public string? NameAr { get; private set; }
    public string TriggerEventType { get; private set; } = default!;
    public int MinimumEventVersion { get; private set; }
    public DateTimeOffset? StartsAtUtc { get; private set; }
    public DateTimeOffset? EndsAtUtc { get; private set; }
    public int Priority { get; private set; }
    public StackingPolicy StackingPolicy { get; private set; }
    public CampaignStatus Status { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public List<CampaignCondition> Conditions { get; private set; } = [];
    public List<CampaignEffect> Effects { get; private set; } = [];

    // Audience targeting (MVP spec §11.5). Lifecycle stages are stored as names to keep the
    // campaigns domain decoupled from the CVM domain.
    public List<string> RequiredLifecycleStages { get; private set; } = [];
    public List<Guid> RequiredSegmentIds { get; private set; } = [];
    public List<Guid> ExcludedSegmentIds { get; private set; } = [];

    // Limits + budget (MVP spec §12).
    public List<MemberLimit> MemberLimits { get; private set; } = [];
    public long? GlobalBudgetInitial { get; private set; }
    public long? GlobalBudgetRemaining { get; private set; }

    private Campaign() { }

    public static Campaign Create(
        Guid id, Guid applicationId, string name, string? nameAr, string triggerEventType, int minimumEventVersion,
        DateTimeOffset? startsAt, DateTimeOffset? endsAt, int priority, StackingPolicy stacking,
        IEnumerable<CampaignCondition> conditions, IEnumerable<CampaignEffect> effects,
        IEnumerable<string>? requiredLifecycleStages, IEnumerable<Guid>? requiredSegmentIds, IEnumerable<Guid>? excludedSegmentIds,
        IEnumerable<MemberLimit>? memberLimits, long? globalBudget,
        DateTimeOffset now) => new()
    {
        MemberLimits = memberLimits?.ToList() ?? [],
        GlobalBudgetInitial = globalBudget,
        GlobalBudgetRemaining = globalBudget,
        Id = id,
        ApplicationId = applicationId,
        Name = name.Trim(),
        NameAr = string.IsNullOrWhiteSpace(nameAr) ? null : nameAr.Trim(),
        TriggerEventType = triggerEventType.Trim(),
        MinimumEventVersion = minimumEventVersion,
        StartsAtUtc = startsAt,
        EndsAtUtc = endsAt,
        Priority = priority,
        StackingPolicy = stacking,
        Status = CampaignStatus.Draft,
        Version = 1,
        Conditions = conditions.ToList(),
        Effects = effects.ToList(),
        RequiredLifecycleStages = requiredLifecycleStages?.ToList() ?? [],
        RequiredSegmentIds = requiredSegmentIds?.ToList() ?? [],
        ExcludedSegmentIds = excludedSegmentIds?.ToList() ?? [],
        CreatedAt = now,
        UpdatedAt = now,
    };

    /// <summary>Audience gate: lifecycle stage + required/excluded segment memberships.</summary>
    public bool MatchesAudience(string? lifecycleStage, IReadOnlySet<Guid> activeSegmentIds)
    {
        if (RequiredLifecycleStages.Count > 0 &&
            (lifecycleStage is null || !RequiredLifecycleStages.Contains(lifecycleStage, StringComparer.OrdinalIgnoreCase)))
            return false;
        if (RequiredSegmentIds.Any(id => !activeSegmentIds.Contains(id))) return false;
        if (ExcludedSegmentIds.Any(activeSegmentIds.Contains)) return false;
        return true;
    }

    public void Validate(DateTimeOffset now)
    {
        if (Status is not (CampaignStatus.Draft or CampaignStatus.Validated))
            throw new InvalidOperationException($"Cannot validate a campaign in status {Status}.");
        Status = CampaignStatus.Validated;
        UpdatedAt = now;
    }

    public void Activate(DateTimeOffset now)
    {
        if (Status is not (CampaignStatus.Validated or CampaignStatus.Paused))
            throw new InvalidOperationException($"Cannot activate a campaign in status {Status}; validate it first.");
        Status = CampaignStatus.Active;
        UpdatedAt = now;
    }

    public void Pause(DateTimeOffset now)
    {
        Status = CampaignStatus.Paused;
        UpdatedAt = now;
    }

    /// <summary>Reserve budget in memory (the atomic DB reservation happens in the handler).</summary>
    public bool TryReserveBudget(long points)
    {
        if (GlobalBudgetRemaining is null) return true; // unlimited
        if (GlobalBudgetRemaining < points) return false;
        GlobalBudgetRemaining -= points;
        return true;
    }

    public bool IsScheduleOpen(DateTimeOffset occurredAt) =>
        (StartsAtUtc is null || occurredAt >= StartsAtUtc)
        && (EndsAtUtc is null || occurredAt <= EndsAtUtc);
}
