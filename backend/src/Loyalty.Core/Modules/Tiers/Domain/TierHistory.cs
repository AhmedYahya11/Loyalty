using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Tiers.Domain;

/// <summary>An immutable record of a member's tier transition (audit).</summary>
public sealed class TierHistory : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid MemberId { get; private set; }
    public Guid? FromTierId { get; private set; }
    public int FromRank { get; private set; }
    public Guid ToTierId { get; private set; }
    public int ToRank { get; private set; }
    public DateTimeOffset EvaluatedAt { get; private set; }

    private TierHistory() { }

    public TierHistory(Guid applicationId, Guid memberId, Guid? fromTierId, int fromRank, Guid toTierId, int toRank, DateTimeOffset evaluatedAt)
    {
        Id = Guid.NewGuid();
        ApplicationId = applicationId;
        MemberId = memberId;
        FromTierId = fromTierId;
        FromRank = fromRank;
        ToTierId = toTierId;
        ToRank = toRank;
        EvaluatedAt = evaluatedAt;
    }
}
