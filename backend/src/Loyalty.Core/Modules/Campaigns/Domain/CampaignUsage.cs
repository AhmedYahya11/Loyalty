using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Campaigns.Domain;

/// <summary>
/// Per-member campaign usage within a limit period (MVP spec §12). UNIQUE(ApplicationId,
/// CampaignId, MemberId, PeriodKey). Incremented when a campaign's effects are applied.
/// </summary>
public sealed class CampaignUsage : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid CampaignId { get; private set; }
    public Guid MemberId { get; private set; }
    public string PeriodKey { get; private set; } = default!;
    public int ExecutionCount { get; private set; }
    public long IssuedPoints { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private CampaignUsage() { }

    public static CampaignUsage Start(Guid applicationId, Guid campaignId, Guid memberId, string periodKey, long issuedPoints, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ApplicationId = applicationId,
        CampaignId = campaignId,
        MemberId = memberId,
        PeriodKey = periodKey,
        ExecutionCount = 1,
        IssuedPoints = issuedPoints,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void Record(long issuedPoints, DateTimeOffset now)
    {
        ExecutionCount++;
        IssuedPoints += issuedPoints;
        UpdatedAt = now;
    }
}
