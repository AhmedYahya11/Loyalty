using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Integration.Domain;

/// <summary>
/// The reward-effect idempotency guard (MVP spec §9.4). One row per applied effect; the
/// unique index UNIQUE(ApplicationId, MemberId, SourceEventId, CampaignId, CampaignVersion,
/// EffectId) makes worker retries / redelivery a no-op at the effect boundary.
/// </summary>
public sealed class RewardEffect : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid MemberId { get; private set; }
    public Guid SourceEventId { get; private set; }
    public Guid CampaignId { get; private set; }
    public int CampaignVersion { get; private set; }
    public string EffectId { get; private set; } = default!;
    public long Points { get; private set; }
    public Guid LedgerTransactionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private RewardEffect() { }

    public RewardEffect(
        Guid id, Guid applicationId, Guid memberId, Guid sourceEventId, Guid campaignId,
        int campaignVersion, string effectId, long points, Guid ledgerTransactionId, DateTimeOffset now)
    {
        Id = id;
        ApplicationId = applicationId;
        MemberId = memberId;
        SourceEventId = sourceEventId;
        CampaignId = campaignId;
        CampaignVersion = campaignVersion;
        EffectId = effectId;
        Points = points;
        LedgerTransactionId = ledgerTransactionId;
        CreatedAt = now;
    }
}
