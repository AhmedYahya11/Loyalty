using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Campaigns.Domain;

/// <summary>A versioned reference to a segment used in a reward decision.</summary>
public sealed record SegmentVersionRef(Guid SegmentId, int Version);

/// <summary>
/// An auditable snapshot of why a reward was (or was not) produced for an event (MVP spec
/// §11.5/§11.6): the profile version, lifecycle stage, segment versions, and effects used. Makes
/// a reward explainable even after the profile or segment rules change.
/// </summary>
public sealed class RewardDecisionRecord : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid MemberId { get; private set; }
    public Guid RequestId { get; private set; }
    public Guid SourceEventId { get; private set; }
    public int ProfileVersion { get; private set; }
    public string LifecycleStage { get; private set; } = default!;
    public List<SegmentVersionRef> SegmentVersions { get; private set; } = [];
    public long AwardedPoints { get; private set; }
    public string Outcome { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }

    private RewardDecisionRecord() { }

    public static RewardDecisionRecord Create(
        Guid applicationId, Guid memberId, Guid requestId, Guid sourceEventId, int profileVersion,
        string lifecycleStage, IEnumerable<SegmentVersionRef> segmentVersions, long awardedPoints, string outcome,
        DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ApplicationId = applicationId,
        MemberId = memberId,
        RequestId = requestId,
        SourceEventId = sourceEventId,
        ProfileVersion = profileVersion,
        LifecycleStage = lifecycleStage,
        SegmentVersions = segmentVersions.ToList(),
        AwardedPoints = awardedPoints,
        Outcome = outcome,
        CreatedAt = now,
    };
}
