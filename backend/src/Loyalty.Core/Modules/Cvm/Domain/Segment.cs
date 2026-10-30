using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Campaigns.Domain; // reuse CampaignCondition + ConditionOperator

namespace Loyalty.Core.Modules.Cvm.Domain;

public enum SegmentStatus
{
    Draft = 0,
    Validated = 1,
    Activating = 2,
    Active = 3,
    Paused = 4,
    Archived = 5,
    ActivationFailed = 6,
}

/// <summary>A rule-based segment definition owned by the CVM domain (MVP spec §11.4).</summary>
public sealed class SegmentDefinition : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? NameAr { get; private set; }
    public SegmentStatus Status { get; private set; }
    public int CurrentVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private SegmentDefinition() { }

    public static SegmentDefinition Create(Guid id, Guid applicationId, string name, string? nameAr, DateTimeOffset now) => new()
    {
        Id = id,
        ApplicationId = applicationId,
        Name = name.Trim(),
        NameAr = string.IsNullOrWhiteSpace(nameAr) ? null : nameAr.Trim(),
        Status = SegmentStatus.Draft,
        CurrentVersion = 1,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void SetStatus(SegmentStatus status, DateTimeOffset now) { Status = status; UpdatedAt = now; }
}

/// <summary>An immutable published version of a segment's conditions. UNIQUE(app, segment, version).</summary>
public sealed class SegmentVersion : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid SegmentId { get; private set; }
    public int Version { get; private set; }
    public List<CampaignCondition> Conditions { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedByAccountId { get; private set; }

    private SegmentVersion() { }

    public static SegmentVersion Create(
        Guid applicationId, Guid segmentId, int version, IEnumerable<CampaignCondition> conditions,
        Guid createdByAccountId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ApplicationId = applicationId,
        SegmentId = segmentId,
        Version = version,
        Conditions = conditions.ToList(),
        CreatedAt = now,
        CreatedByAccountId = createdByAccountId,
    };
}

/// <summary>A member's membership in a segment. One active row per (app, member, segment).</summary>
public sealed class MemberSegmentMembership : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid MemberId { get; private set; }
    public Guid SegmentId { get; private set; }
    public int SegmentVersion { get; private set; }
    public int EnteredProfileVersion { get; private set; }
    public int LastEvaluatedProfileVersion { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset EnteredAt { get; private set; }
    public DateTimeOffset? ExitedAt { get; private set; }
    public DateTimeOffset EvaluatedAt { get; private set; }

    private MemberSegmentMembership() { }

    public static MemberSegmentMembership Enter(
        Guid applicationId, Guid memberId, Guid segmentId, int segmentVersion, int profileVersion, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ApplicationId = applicationId,
        MemberId = memberId,
        SegmentId = segmentId,
        SegmentVersion = segmentVersion,
        EnteredProfileVersion = profileVersion,
        LastEvaluatedProfileVersion = profileVersion,
        IsActive = true,
        EnteredAt = now,
        EvaluatedAt = now,
    };

    public void ReEvaluate(int profileVersion, DateTimeOffset now) { LastEvaluatedProfileVersion = profileVersion; EvaluatedAt = now; }
    public void Exit(int profileVersion, DateTimeOffset now) { IsActive = false; ExitedAt = now; LastEvaluatedProfileVersion = profileVersion; EvaluatedAt = now; }
}
