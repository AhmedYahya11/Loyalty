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
    public static async Task Handle(ProcessLoyaltyEvent message, IServiceScopeFactory scopeFactory, CancellationToken ct)
    {
        using var di = scopeFactory.CreateScope();
        var sp = di.ServiceProvider;
        var scope = sp.GetRequiredService<ApplicationContext>();
        var db = sp.GetRequiredService<LoyaltyDbContext>();
        var scopeResolver = sp.GetRequiredService<ApplicationScopeResolver>();
        var registry = sp.GetRequiredService<EventRegistry>();
        var provisioning = sp.GetRequiredService<MemberProvisioningService>();
        var ledger = sp.GetRequiredService<LedgerService>();
        var decider = sp.GetRequiredService<RewardDecisionService>();
        var engine = sp.GetRequiredService<CampaignEngine>();
        var cvm = sp.GetRequiredService<CvmService>();
        var gamification = sp.GetRequiredService<Gamification.Application.GamificationService>();
        var clock = sp.GetRequiredService<IClock>();

        var visible = await RunInSystemScope(scope, () => scopeResolver.ResolveVisibleAsync(message.ApplicationId, ct));
        scope.Establish(message.ApplicationId, message.AccountId, sourceSystem: string.Empty, visible);

        var request = await db.IngestionRequests.FirstOrDefaultAsync(r => r.Id == message.RequestId, ct)
            ?? throw new InvalidOperationException($"Ingestion request {message.RequestId} not found.");

        if (request.Status is RequestStatus.Completed or RequestStatus.Rejected or RequestStatus.Failed)
            return;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = clock.UtcNow;
        request.MarkProcessing(now);

        var envelope = JsonSerializer.Deserialize<LoyaltyEventEnvelopeV1>(request.Payload, JsonOptionsProvider.Default)!;
        var normalized = registry.Normalize(envelope, request.SourceSystem);
        var snapshot = JsonSerializer.Deserialize<SettingsSnapshotDto>(request.SettingsSnapshot, JsonOptionsProvider.Default)!;

        // provision member + wallet + ledger accounts + value profile (idempotent); persist
        var (member, wallet) = await provisioning.EnsureAsync(
            request.ApplicationId, envelope.CustomerId, snapshot.PointsUnitCode, snapshot.BaseCurrency, ct);
        await db.SaveChangesAsync(ct);

        // --- CVM: project event onto the value profile, evaluate lifecycle (frozen) + segments ---
        var profile = await db.ValueProfiles.FirstAsync(p => p.MemberId == member.Id, ct);
        var previousStage = profile.LifecycleStage;
        cvm.ApplyEvent(profile, envelope.EventType, normalized.Amount, envelope.OccurredAt);
        if (envelope.EventType != EventTypes.ManualAdjustmentRequested)
            profile.ApplyStreak(envelope.OccurredAt); // daily-activity streak
        var thresholds = ParseCvmThresholds(request.CvmSettingsSnapshot);
        var lifecycleChanged = cvm.EvaluateLifecycle(profile, thresholds, now);
        await cvm.EvaluateSegmentsAsync(profile, now, ct);
        await db.SaveChangesAsync(ct);

        if (lifecycleChanged)
            EmitEvent(db, request, ResultEventTypes.MemberLifecycleChanged, member.Id,
                new { memberId = member.Id, from = previousStage.ToString(), to = profile.LifecycleStage.ToString() }, now);

        var activeMemberships = await db.SegmentMemberships
            .Where(m => m.MemberId == member.Id && m.IsActive).ToListAsync(ct);
        var activeSegmentIds = activeMemberships.Select(m => m.SegmentId).ToHashSet();
        var segmentVersionRefs = activeMemberships.Select(m => new SegmentVersionRef(m.SegmentId, m.SegmentVersion)).ToList();

        // --- Tiers: evaluate the member's tier; apply its earn-multiplier to the reward ---
        var tiers = await db.Tiers.Where(t => t.ApplicationId == request.ApplicationId && t.IsActive).ToListAsync(ct);
        var newTier = Tiers.Domain.TierEvaluator.Evaluate(profile.NetSpendAmount, profile.PointsEarned, profile.PurchaseCount, tiers);
        var newRank = newTier?.Rank ?? -1;
        if (newRank != profile.CurrentTierRank)
        {
            var fromTierId = profile.CurrentTierId;
            var fromRank = profile.CurrentTierRank;
            profile.SetTier(newTier?.Id, newRank, now);
            if (newTier is not null)
            {
                db.TierHistory.Add(new Tiers.Domain.TierHistory(request.ApplicationId, member.Id, fromTierId, fromRank, newTier.Id, newRank, now));
                EmitEvent(db, request, ResultEventTypes.MemberTierChanged, member.Id,
                    new { memberId = member.Id, fromRank, toRank = newRank, tierName = newTier.Name }, now);
            }
            await db.SaveChangesAsync(ct);
        }
        var tierMultiplier = newTier?.EarnMultiplier ?? 1m;

        // --- ManualAdjustment: controlled Adjust ledger entry, no campaigns ---
        if (envelope.EventType == EventTypes.ManualAdjustmentRequested)
        {
            var signed = normalized.SignedPoints ?? 0;
            var alreadyAdjusted = await db.RewardEffects.AnyAsync(
                e => e.MemberId == member.Id && e.SourceEventId == envelope.EventId && e.EffectId == "manual-adjustment", ct);
            if (!alreadyAdjusted && signed != 0)
            {
                var adjTx = await ledger.PostAdjustAsync(wallet, signed, envelope.EventId, request.SourceSystem,
                    envelope.SourceTransactionId, normalized.BusinessEventKey, envelope.CorrelationId, "manual-adjustment", envelope.OccurredAt, ct);
                db.RewardEffects.Add(new RewardEffect(Guid.NewGuid(), request.ApplicationId, member.Id, envelope.EventId,
                    Guid.Empty, 0, "manual-adjustment", signed, adjTx.Id, now));
                EmitEvent(db, request, ResultEventTypes.PointsAdjusted, member.Id,
                    new { memberId = member.Id, requestId = request.Id, signedPoints = signed, pointsUnitCode = snapshot.PointsUnitCode }, now);
            }
            request.MarkCompleted(signed, snapshot.PointsUnitCode, clock.UtcNow);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return;
        }

        // --- OrderRefunded: compensating reversal of the original earn(s) ---
        if (envelope.EventType == EventTypes.OrderRefunded)
        {
            normalized.Facts.TryGetValue("attributes.originalOrderId", out var originalOrderObj);
            var originalKey = $"{request.SourceSystem}|{originalOrderObj}";
            var originalEarns = await db.LedgerTransactions
                .Where(t => t.WalletId == wallet.Id && t.TransactionType == Wallets.Domain.TransactionType.Earn && t.BusinessEventKey == originalKey)
                .ToListAsync(ct);

            long reversed = 0;
            foreach (var earn in originalEarns)
            {
                var effectId = $"refund-reversal:{earn.Id}";
                if (await db.RewardEffects.AnyAsync(e => e.MemberId == member.Id && e.SourceEventId == envelope.EventId && e.EffectId == effectId, ct))
                    continue;
                var revTx = await ledger.PostReverseAsync(wallet, earn, envelope.EventId, request.SourceSystem,
                    envelope.SourceTransactionId, normalized.BusinessEventKey, envelope.CorrelationId, effectId, envelope.OccurredAt, ct);
                db.RewardEffects.Add(new RewardEffect(Guid.NewGuid(), request.ApplicationId, member.Id, envelope.EventId,
                    earn.CampaignId ?? Guid.Empty, 0, effectId, -earn.TotalCredit, revTx.Id, now));
                reversed += earn.TotalCredit;
            }
            if (reversed > 0)
                EmitEvent(db, request, ResultEventTypes.PointsReversed, member.Id,
                    new { memberId = member.Id, requestId = request.Id, reversedPoints = reversed, pointsUnitCode = snapshot.PointsUnitCode }, now);
            request.MarkCompleted(-reversed, snapshot.PointsUnitCode, clock.UtcNow);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return;
        }

        // --- campaign reward path: eligibility → member limits → budget → stacked effects ---
        var candidates = await db.Campaigns
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

        foreach (var c in limitSurviving)
        {
            if (!byCampaign.TryGetValue(c.Id, out var campaignEffects)) continue;
            var campaignPoints = campaignEffects.Sum(e => e.Points);
            if (!await engine.TryReserveBudgetAsync(c, campaignPoints, ct)) continue; // budget exhausted → skip

            foreach (var effect in campaignEffects)
            {
                var alreadyApplied = await db.RewardEffects.AnyAsync(
                    e => e.MemberId == member.Id && e.SourceEventId == envelope.EventId
                         && e.CampaignId == effect.CampaignId && e.CampaignVersion == effect.CampaignVersion
                         && e.EffectId == effect.EffectId, ct);
                if (alreadyApplied) continue;

                var ledgerTx = await ledger.PostEarnAsync(
                    wallet, effect.Points, envelope.EventId, request.SourceSystem, envelope.SourceTransactionId,
                    normalized.BusinessEventKey, envelope.CorrelationId, effect.CampaignId, effect.EffectId, envelope.OccurredAt, ct);

                db.RewardEffects.Add(new RewardEffect(
                    Guid.NewGuid(), request.ApplicationId, member.Id, envelope.EventId, effect.CampaignId,
                    effect.CampaignVersion, effect.EffectId, effect.Points, ledgerTx.Id, clock.UtcNow));
                totalAwarded += effect.Points;
            }
            appliedCampaigns.Add(c.Id);
            await engine.RecordUsageAsync(c, member.Id, envelope.OccurredAt, campaignPoints, ct);
        }

        if (totalAwarded > 0) profile.AddPointsEarned(totalAwarded);

        // gamification runs regardless of a campaign reward (streak/achievements/challenges)
        var gam = await gamification.EvaluateAsync(wallet, member, profile, envelope.EventType, normalized.Amount,
            envelope.EventId, request.SourceSystem, envelope.SourceTransactionId, normalized.BusinessEventKey, envelope.CorrelationId,
            envelope.OccurredAt, now, ct);
        var grandTotal = totalAwarded + gam.Bonus;

        if (grandTotal <= 0 && !gam.Any)
        {
            db.RewardDecisions.Add(RewardDecisionRecord.Create(request.ApplicationId, member.Id, request.Id, envelope.EventId,
                profile.Version, profile.LifecycleStage.ToString(), segmentVersionRefs, 0, "Rejected", now));
            request.MarkRejected("NO_ELIGIBLE_CAMPAIGN", clock.UtcNow);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return;
        }

        db.RewardDecisions.Add(RewardDecisionRecord.Create(request.ApplicationId, member.Id, request.Id, envelope.EventId,
            profile.Version, profile.LifecycleStage.ToString(), segmentVersionRefs, grandTotal, "Completed", now));
        if (grandTotal > 0)
            EmitEvent(db, request, ResultEventTypes.PointsAwarded, member.Id,
                new { memberId = member.Id, requestId = request.Id, awardedPoints = grandTotal, pointsUnitCode = snapshot.PointsUnitCode }, now);
        request.MarkCompleted(grandTotal, snapshot.PointsUnitCode, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static async Task<T> RunInSystemScope<T>(ApplicationContext scope, Func<Task<T>> action)
    {
        scope.EnterSystemScope();
        try { return await action(); }
        finally { scope.LeaveSystemScope(); }
    }

    /// <summary>Write a result event to the transactional outbox (event log).</summary>
    private static void EmitEvent(LoyaltyDbContext db, IngestionRequest request, string eventType, Guid memberId, object payload, DateTimeOffset now)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptionsProvider.Default);
        db.EventLog.Add(EventLogEntry.Create(request.ApplicationId, eventType, memberId, request.Id, json, request.OccurredAt, now));
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
