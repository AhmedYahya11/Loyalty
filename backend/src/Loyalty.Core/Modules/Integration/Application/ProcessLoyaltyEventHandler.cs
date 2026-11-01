using System.Text.Json;
using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Applications.Application;
using Loyalty.Core.Modules.Campaigns.Application;
using Loyalty.Core.Modules.Campaigns.Domain;
using Loyalty.Core.Modules.Cvm.Application;
using Loyalty.Core.Modules.Integration.Contracts;
using Loyalty.Core.Modules.Integration.Domain;
using Loyalty.Core.Modules.Cvm.Domain;
using Loyalty.Core.Modules.Members.Application;
using Loyalty.Core.Modules.Notifications.Domain;
using Loyalty.Core.Modules.Settings.Domain;
using Loyalty.Core.Modules.Wallets.Application;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Loyalty.Core.Modules.Integration.Application;

/// <summary>Frozen effective-settings snapshot stored on the ingestion request (§10).</summary>
public sealed record SettingsSnapshotDto(
    Guid SourceApplicationId, int Version, string PointsUnitCode, string BaseCurrency,
    decimal SpendAmountPerPoint, RoundingPolicy RoundingPolicy, LoyaltyStatus LoyaltyStatus);

/// <summary>
/// The single asynchronous processing handler (MVP spec §14, PROCESSING_FLOW diagram 3):
/// restore scope, provision the member, apply the event to its value profile, evaluate
/// lifecycle + segments, select audience-targeted campaigns, write balanced ledger postings
/// and the wallet projection, and update request status — all in one transaction. Redelivery
/// is a no-op via the terminal-status check and the reward-effect / ledger unique indexes.
/// </summary>
public sealed class ProcessLoyaltyEventHandler
{
    public static async Task Handle(
        ProcessLoyaltyEvent message, IServiceScopeFactory scopeFactory, CancellationToken ct)
    {
        using var di = scopeFactory.CreateScope();
        var sp = di.ServiceProvider;
        // Repos + unit of work MUST be resolved from THIS handler scope so they share the single
        // DbContext that the business services below also resolve — the explicit transaction and the
        // Wolverine EF outbox then span one context (Wolverine method-injection would use a different scope).
        var ingestionRequestsRepo = sp.GetRequiredService<IRepository<Loyalty.Core.Modules.Integration.Domain.IngestionRequest>>();
        var valueProfilesRepo = sp.GetRequiredService<IRepository<Loyalty.Core.Modules.Cvm.Domain.MemberValueProfile>>();
        var segmentMembershipsRepo = sp.GetRequiredService<IRepository<Loyalty.Core.Modules.Cvm.Domain.MemberSegmentMembership>>();
        var tiersRepo = sp.GetRequiredService<IRepository<Loyalty.Core.Modules.Tiers.Domain.Tier>>();
        var tierHistoryRepo = sp.GetRequiredService<IRepository<Loyalty.Core.Modules.Tiers.Domain.TierHistory>>();
        var rewardEffectsRepo = sp.GetRequiredService<IRepository<Loyalty.Core.Modules.Integration.Domain.RewardEffect>>();
        var ledgerTransactionsRepo = sp.GetRequiredService<IRepository<Loyalty.Core.Modules.Wallets.Domain.PointsLedgerTransaction>>();
        var campaignsRepo = sp.GetRequiredService<IRepository<Loyalty.Core.Modules.Campaigns.Domain.Campaign>>();
        var rewardDecisionsRepo = sp.GetRequiredService<IRepository<Loyalty.Core.Modules.Campaigns.Domain.RewardDecisionRecord>>();
        var eventLogRepo = sp.GetRequiredService<IRepository<Loyalty.Core.Modules.Notifications.Domain.EventLogEntry>>();
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var scope = sp.GetRequiredService<ApplicationContext>();
        var scopeResolver = sp.GetRequiredService<ApplicationScopeResolver>();
        var registry = sp.GetRequiredService<EventRegistry>();
        var provisioning = sp.GetRequiredService<MemberProvisioningService>();
        var ledger = sp.GetRequiredService<LedgerService>();
        var decider = sp.GetRequiredService<RewardDecisionService>();
        var engine = sp.GetRequiredService<CampaignEngine>();
        var cvm = sp.GetRequiredService<CvmService>();
        var gamification = sp.GetRequiredService<Gamification.Application.GamificationService>();
        var referrals = sp.GetRequiredService<Engagement.Application.ReferralService>();
        var stampCards = sp.GetRequiredService<Engagement.Application.StampCardService>();
        var raffles = sp.GetRequiredService<Raffles.Application.RaffleService>();
        var redemptions = sp.GetRequiredService<Rewards.Application.RedemptionService>();
        var clock = sp.GetRequiredService<IClock>();

        var visible = await RunInSystemScope(scope, () => scopeResolver.ResolveVisibleAsync(message.ApplicationId, ct));
        scope.Establish(message.ApplicationId, message.AccountId, sourceSystem: string.Empty, visible);

        var request = await ingestionRequestsRepo.Query().FirstOrDefaultAsync(r => r.Id == message.RequestId, ct)
            ?? throw new InvalidOperationException($"Ingestion request {message.RequestId} not found.");

        if (request.Status is RequestStatus.Completed or RequestStatus.Rejected or RequestStatus.Failed)
            return;

        await using var tx = await uow.BeginTransactionAsync(ct);
        var now = clock.UtcNow;
        request.MarkProcessing(now);

        var envelope = JsonSerializer.Deserialize<LoyaltyEventEnvelopeV1>(request.Payload, JsonOptionsProvider.Default)!;
        var normalized = registry.Normalize(envelope, request.SourceSystem);
        var snapshot = JsonSerializer.Deserialize<SettingsSnapshotDto>(request.SettingsSnapshot, JsonOptionsProvider.Default)!;

        // provision member + wallet + ledger accounts + value profile (idempotent); persist
        var (member, wallet) = await provisioning.EnsureAsync(
            request.ApplicationId, envelope.CustomerId, snapshot.PointsUnitCode, snapshot.BaseCurrency, ct);
        await uow.SaveChangesAsync(ct);

        // --- CVM: project event onto the value profile, evaluate lifecycle (frozen) + segments ---
        var profile = await valueProfilesRepo.Query().FirstAsync(p => p.MemberId == member.Id, ct);
        var previousStage = profile.LifecycleStage;
        cvm.ApplyEvent(profile, envelope.EventType, normalized.Amount, envelope.OccurredAt);
        if (envelope.EventType != EventTypes.ManualAdjustmentRequested)
            profile.ApplyStreak(envelope.OccurredAt); // daily-activity streak
        var thresholds = ParseCvmThresholds(request.CvmSettingsSnapshot);
        var lifecycleChanged = cvm.EvaluateLifecycle(profile, thresholds, now);
        await cvm.EvaluateSegmentsAsync(profile, now, ct);
        await uow.SaveChangesAsync(ct);

        if (lifecycleChanged)
            EmitEvent(eventLogRepo, request, ResultEventTypes.MemberLifecycleChanged, member.Id,
                new { memberId = member.Id, from = previousStage.ToString(), to = profile.LifecycleStage.ToString() }, now);

        var activeMemberships = await segmentMembershipsRepo.Query()
            .Where(m => m.MemberId == member.Id && m.IsActive).ToListAsync(ct);
        var activeSegmentIds = activeMemberships.Select(m => m.SegmentId).ToHashSet();
        var segmentVersionRefs = activeMemberships.Select(m => new SegmentVersionRef(m.SegmentId, m.SegmentVersion)).ToList();

        // --- Tiers: evaluate the member's tier; apply its earn-multiplier to the reward ---
        var tiers = await tiersRepo.Query().Where(t => t.ApplicationId == request.ApplicationId && t.IsActive).ToListAsync(ct);
        var newTier = Tiers.Domain.TierEvaluator.Evaluate(profile.NetSpendAmount, profile.PointsEarned, profile.PurchaseCount, tiers);
        var newRank = newTier?.Rank ?? -1;
        if (newRank != profile.CurrentTierRank)
        {
            var fromTierId = profile.CurrentTierId;
            var fromRank = profile.CurrentTierRank;
            profile.SetTier(newTier?.Id, newRank, now);
            if (newTier is not null)
            {
                tierHistoryRepo.Add(new Tiers.Domain.TierHistory(request.ApplicationId, member.Id, fromTierId, fromRank, newTier.Id, newRank, now));
                EmitEvent(eventLogRepo, request, ResultEventTypes.MemberTierChanged, member.Id,
                    new { memberId = member.Id, fromRank, toRank = newRank, tierName = newTier.Name }, now);
            }
            await uow.SaveChangesAsync(ct);
        }
        var tierMultiplier = newTier?.EarnMultiplier ?? 1m;

        // --- ManualAdjustment: controlled Adjust ledger entry, no campaigns ---
        if (envelope.EventType == EventTypes.ManualAdjustmentRequested)
        {
            var signed = normalized.SignedPoints ?? 0;
            var alreadyAdjusted = await rewardEffectsRepo.Query().AnyAsync(
                e => e.MemberId == member.Id && e.SourceEventId == envelope.EventId && e.EffectId == "manual-adjustment", ct);
            if (!alreadyAdjusted && signed != 0)
            {
                var adjTx = await ledger.PostAdjustAsync(wallet, signed, envelope.EventId, request.SourceSystem,
                    envelope.SourceTransactionId, normalized.BusinessEventKey, envelope.CorrelationId, "manual-adjustment", envelope.OccurredAt, ct);
                rewardEffectsRepo.Add(new RewardEffect(Guid.NewGuid(), request.ApplicationId, member.Id, envelope.EventId,
                    Guid.Empty, 0, "manual-adjustment", signed, adjTx.Id, now));
                EmitEvent(eventLogRepo, request, ResultEventTypes.PointsAdjusted, member.Id,
                    new { memberId = member.Id, requestId = request.Id, signedPoints = signed, pointsUnitCode = snapshot.PointsUnitCode }, now);
            }
            request.MarkCompleted(signed, snapshot.PointsUnitCode, clock.UtcNow);
            await uow.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return;
        }

        // --- OrderRefunded: compensating reversal of the original earn(s) ---
        if (envelope.EventType == EventTypes.OrderRefunded)
        {
            normalized.Facts.TryGetValue("attributes.originalOrderId", out var originalOrderObj);
            var originalKey = $"{request.SourceSystem}|{originalOrderObj}";
            var originalEarns = await ledgerTransactionsRepo.Query()
                .Where(t => t.WalletId == wallet.Id && t.TransactionType == Wallets.Domain.TransactionType.Earn && t.BusinessEventKey == originalKey)
                .ToListAsync(ct);

            long reversed = 0;
            foreach (var earn in originalEarns)
            {
                var effectId = $"refund-reversal:{earn.Id}";
                if (await rewardEffectsRepo.Query().AnyAsync(e => e.MemberId == member.Id && e.SourceEventId == envelope.EventId && e.EffectId == effectId, ct))
                    continue;
                var revTx = await ledger.PostReverseAsync(wallet, earn, envelope.EventId, request.SourceSystem,
                    envelope.SourceTransactionId, normalized.BusinessEventKey, envelope.CorrelationId, effectId, envelope.OccurredAt, ct);
                rewardEffectsRepo.Add(new RewardEffect(Guid.NewGuid(), request.ApplicationId, member.Id, envelope.EventId,
                    earn.CampaignId ?? Guid.Empty, 0, effectId, -earn.TotalCredit, revTx.Id, now));
                reversed += earn.TotalCredit;
            }
            if (reversed > 0)
                EmitEvent(eventLogRepo, request, ResultEventTypes.PointsReversed, member.Id,
                    new { memberId = member.Id, requestId = request.Id, reversedPoints = reversed, pointsUnitCode = snapshot.PointsUnitCode }, now);

            // P14: a refunded purchase also unwinds the stamps it earned + decrements open challenge progress.
            // Resolve the original purchase event(s) from the request log so this works even without earns.
            var originalEventIds = await ingestionRequestsRepo.Query()
                .Where(r => r.BusinessEventKey == originalKey && r.EventType == EventTypes.PurchaseCompleted)
                .Select(r => r.EventId).ToListAsync(ct);
            await stampCards.ReverseForEventsAsync(wallet, member, profile, originalEventIds, envelope.EventId, now, ct);
            await gamification.ReverseChallengesAsync(member, normalized.Amount, envelope.OccurredAt, now, ct);

            request.MarkCompleted(-reversed, snapshot.PointsUnitCode, clock.UtcNow);
            await uow.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return;
        }

        // --- campaign reward path: eligibility → member limits → budget → stacked effects ---
        var candidates = await campaignsRepo.Query()
            .Where(c => c.Status == CampaignStatus.Active
                        && c.TriggerEventType == envelope.EventType
                        && c.MinimumEventVersion <= envelope.EventVersion)
            .ToListAsync(ct);
        candidates = candidates.Where(c => c.IsScheduleOpen(envelope.OccurredAt)).ToList();

        var eligible = decider.SelectEligible(normalized, candidates, profile.LifecycleStage.ToString(), activeSegmentIds);
        var limitSurviving = await engine.FilterByMemberLimitsAsync(eligible, member.Id, envelope.OccurredAt, ct);

        var decision = decider.ComputeStackedEffects(snapshot.SpendAmountPerPoint, snapshot.RoundingPolicy, normalized, limitSurviving, tierMultiplier);

        // group effects by campaign; reserve budget per campaign (atomic), drop campaigns that fail
        var byCampaign = decision.Effects.GroupBy(e => e.CampaignId).ToDictionary(g => g.Key, g => g.ToList());
        var appliedCampaigns = new HashSet<Guid>();
        long totalAwarded = 0;
        var rewardsGranted = 0;

        foreach (var c in limitSurviving)
        {
            var applied = false;
            long campaignPoints = 0;

            // --- point effects (budget-gated) ---
            if (byCampaign.TryGetValue(c.Id, out var campaignEffects))
            {
                campaignPoints = campaignEffects.Sum(e => e.Points);
                if (await engine.TryReserveBudgetAsync(c, campaignPoints, ct)) // budget exhausted → skip points
                {
                    foreach (var effect in campaignEffects)
                    {
                        var alreadyApplied = await rewardEffectsRepo.Query().AnyAsync(
                            e => e.MemberId == member.Id && e.SourceEventId == envelope.EventId
                                 && e.CampaignId == effect.CampaignId && e.CampaignVersion == effect.CampaignVersion
                                 && e.EffectId == effect.EffectId, ct);
                        if (alreadyApplied) continue;

                        var ledgerTx = await ledger.PostEarnAsync(
                            wallet, effect.Points, envelope.EventId, request.SourceSystem, envelope.SourceTransactionId,
                            normalized.BusinessEventKey, envelope.CorrelationId, effect.CampaignId, effect.EffectId, envelope.OccurredAt, ct);

                        rewardEffectsRepo.Add(new RewardEffect(
                            Guid.NewGuid(), request.ApplicationId, member.Id, envelope.EventId, effect.CampaignId,
                            effect.CampaignVersion, effect.EffectId, effect.Points, ledgerTx.Id, clock.UtcNow));
                        totalAwarded += effect.Points;
                    }
                    applied = true;
                }
            }

            // --- grant-reward effects: issue a free coupon/voucher (P16) ---
            foreach (var ge in c.Effects.Where(e => e.Type == Campaigns.Domain.EffectType.GrantReward && e.RewardId is not null))
            {
                var alreadyGranted = await rewardEffectsRepo.Query().AnyAsync(
                    e => e.MemberId == member.Id && e.SourceEventId == envelope.EventId
                         && e.CampaignId == c.Id && e.CampaignVersion == c.Version && e.EffectId == ge.EffectId, ct);
                if (alreadyGranted) continue;

                var grant = await redemptions.GrantAsync(member.Id, ge.RewardId!.Value, now, ct);
                if (grant is null) continue; // out of stock / per-member limit reached
                rewardEffectsRepo.Add(new RewardEffect(Guid.NewGuid(), request.ApplicationId, member.Id, envelope.EventId,
                    c.Id, c.Version, ge.EffectId, 0, Guid.Empty, clock.UtcNow));
                rewardsGranted++;
                applied = true;
            }

            if (applied)
            {
                appliedCampaigns.Add(c.Id);
                await engine.RecordUsageAsync(c, member.Id, envelope.OccurredAt, campaignPoints, ct);
            }
        }

        if (totalAwarded > 0) profile.AddPointsEarned(totalAwarded);

        // stamp cards: add a stamp for qualifying events; award + reset when a card fills (P10)
        var stamp = await stampCards.EvaluateAsync(wallet, member, profile, normalized,
            envelope.EventId, request.SourceSystem, envelope.SourceTransactionId, normalized.BusinessEventKey, envelope.CorrelationId,
            envelope.OccurredAt, now, ct);

        // referrals: reward both sides when a referral completes (P10)
        var referral = envelope.EventType == EventTypes.ReferralCompleted
            ? await referrals.EvaluateAsync(wallet, member, profile, normalized,
                envelope.EventId, request.SourceSystem, envelope.SourceTransactionId, normalized.BusinessEventKey, envelope.CorrelationId,
                envelope.OccurredAt, now, ct)
            : new Engagement.Application.ReferralResult(0, 0, false);

        // gamification runs regardless of a campaign reward (streak/achievements/challenges)
        var gam = await gamification.EvaluateAsync(wallet, member, profile, envelope.EventType, normalized.Amount,
            envelope.EventId, request.SourceSystem, envelope.SourceTransactionId, normalized.BusinessEventKey, envelope.CorrelationId,
            envelope.OccurredAt, now, ct);
        // raffles: grant weighted entries for qualifying events (purchase ≥ value, custom actions, …)
        var raffle = await raffles.EvaluateAsync(wallet, member, normalized, envelope.EventId, now, ct);

        var grandTotal = totalAwarded + stamp.Bonus + referral.RefereeBonus + gam.Bonus;
        var anyEngagement = gam.Any || stamp.Completed > 0 || referral.Rewarded || raffle.EntriesGranted > 0 || rewardsGranted > 0;

        if (grandTotal <= 0 && !anyEngagement)
        {
            rewardDecisionsRepo.Add(RewardDecisionRecord.Create(request.ApplicationId, member.Id, request.Id, envelope.EventId,
                profile.Version, profile.LifecycleStage.ToString(), segmentVersionRefs, 0, "Rejected", now));
            request.MarkRejected("NO_ELIGIBLE_CAMPAIGN", clock.UtcNow);
            await uow.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return;
        }

        rewardDecisionsRepo.Add(RewardDecisionRecord.Create(request.ApplicationId, member.Id, request.Id, envelope.EventId,
            profile.Version, profile.LifecycleStage.ToString(), segmentVersionRefs, grandTotal, "Completed", now));
        if (grandTotal > 0)
            EmitEvent(eventLogRepo, request, ResultEventTypes.PointsAwarded, member.Id,
                new { memberId = member.Id, requestId = request.Id, awardedPoints = grandTotal, pointsUnitCode = snapshot.PointsUnitCode }, now);
        request.MarkCompleted(grandTotal, snapshot.PointsUnitCode, clock.UtcNow);
        await uow.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static async Task<T> RunInSystemScope<T>(ApplicationContext scope, Func<Task<T>> action)
    {
        scope.EnterSystemScope();
        try { return await action(); }
        finally { scope.LeaveSystemScope(); }
    }

    /// <summary>Write a result event to the transactional outbox (event log).</summary>
    private static void EmitEvent(IRepository<Loyalty.Core.Modules.Notifications.Domain.EventLogEntry> eventLogRepo, IngestionRequest request, string eventType, Guid memberId, object payload, DateTimeOffset now)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptionsProvider.Default);
        eventLogRepo.Add(EventLogEntry.Create(request.ApplicationId, eventType, memberId, request.Id, json, request.OccurredAt, now));
    }

    private sealed record CvmSnapshotParse(string CvmStatus, int NewPeriodDays, int AtRiskAfterDays, int DormantAfterDays, int ChurnedAfterDays);

    /// <summary>Parse frozen CVM thresholds from the request snapshot; null when absent/inactive.</summary>
    private static CvmThresholds? ParseCvmThresholds(string? snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot)) return null;
        var dto = JsonSerializer.Deserialize<CvmSnapshotParse>(snapshot, JsonOptionsProvider.Default);
        if (dto is null || !string.Equals(dto.CvmStatus, "Active", StringComparison.OrdinalIgnoreCase)) return null;
        return new CvmThresholds(dto.NewPeriodDays, dto.AtRiskAfterDays, dto.DormantAfterDays, dto.ChurnedAfterDays);
    }
}
