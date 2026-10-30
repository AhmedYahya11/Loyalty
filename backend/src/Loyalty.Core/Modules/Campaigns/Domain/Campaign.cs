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

    private Campaign() { }

    public static Campaign Create(
        Guid id, Guid applicationId, string name, string? nameAr, string triggerEventType, int minimumEventVersion,
        DateTimeOffset? startsAt, DateTimeOffset? endsAt, int priority, StackingPolicy stacking,
        IEnumerable<CampaignCondition> conditions, IEnumerable<CampaignEffect> effects,
        DateTimeOffset now) => new()
    {
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
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void Activate(DateTimeOffset now)
    {
        Status = CampaignStatus.Active;
        UpdatedAt = now;
    }

    public void Pause(DateTimeOffset now)
    {
        Status = CampaignStatus.Paused;
        UpdatedAt = now;
    }

    public bool IsScheduleOpen(DateTimeOffset occurredAt) =>
        (StartsAtUtc is null || occurredAt >= StartsAtUtc)
        && (EndsAtUtc is null || occurredAt <= EndsAtUtc);
}
