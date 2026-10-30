using System.Text.Json;
using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Applications.Application;
using Loyalty.Core.Modules.Campaigns.Application;
using Loyalty.Core.Modules.Campaigns.Domain;
using Loyalty.Core.Modules.Cvm.Application;
using Loyalty.Core.Modules.Integration.Contracts;
using Loyalty.Core.Modules.Integration.Domain;
using Loyalty.Core.Modules.Members.Application;
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
        var cvm = sp.GetRequiredService<CvmService>();
        var cvmSettings = sp.GetRequiredService<CvmSettingsService>();
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

        // --- CVM: project event onto the value profile, evaluate lifecycle + segments ---
        var profile = await db.ValueProfiles.FirstAsync(p => p.MemberId == member.Id, ct);
        cvm.ApplyEvent(profile, envelope.EventType, normalized.Amount, envelope.OccurredAt);
        var effectiveCvm = await cvmSettings.ResolveEffectiveAsync(request.ApplicationId, ct);
        cvm.EvaluateLifecycle(profile, effectiveCvm, now);
        await cvm.EvaluateSegmentsAsync(profile, now, ct);
        await db.SaveChangesAsync(ct);

        var activeSegmentIds = (await db.SegmentMemberships
            .Where(m => m.MemberId == member.Id && m.IsActive).Select(m => m.SegmentId).ToListAsync(ct)).ToHashSet();

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
            }
            request.MarkCompleted(signed, snapshot.PointsUnitCode, clock.UtcNow);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return;
        }

        // --- select audience-targeted campaigns for this event type ---
        var candidates = await db.Campaigns
            .Where(c => c.Status == CampaignStatus.Active
                        && c.TriggerEventType == envelope.EventType
                        && c.MinimumEventVersion <= envelope.EventVersion)
            .ToListAsync(ct);
        candidates = candidates.Where(c => c.IsScheduleOpen(envelope.OccurredAt)).ToList();

        var decision = decider.Decide(
            snapshot.SpendAmountPerPoint, snapshot.RoundingPolicy, normalized, candidates,
            profile.LifecycleStage.ToString(), activeSegmentIds);

        if (!decision.HasReward)
        {
            // the value-profile changes still commit; only the reward is absent
            request.MarkRejected("NO_ELIGIBLE_CAMPAIGN", clock.UtcNow);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return;
        }

        long totalAwarded = 0;
        foreach (var effect in decision.Effects)
        {
            var alreadyApplied = await db.RewardEffects.AnyAsync(
                e => e.MemberId == member.Id && e.SourceEventId == envelope.EventId
                     && e.CampaignId == effect.CampaignId && e.CampaignVersion == effect.CampaignVersion
                     && e.EffectId == effect.EffectId, ct);
            if (alreadyApplied) continue;

            var ledgerTx = await ledger.PostEarnAsync(
                wallet, effect.Points, envelope.EventId, request.SourceSystem, envelope.SourceTransactionId,
                normalized.BusinessEventKey, envelope.CorrelationId, effect.CampaignId, effect.EffectId,
                envelope.OccurredAt, ct);

            db.RewardEffects.Add(new RewardEffect(
                Guid.NewGuid(), request.ApplicationId, member.Id, envelope.EventId, effect.CampaignId,
                effect.CampaignVersion, effect.EffectId, effect.Points, ledgerTx.Id, clock.UtcNow));

            totalAwarded += effect.Points;
        }

        if (totalAwarded > 0) profile.AddPointsEarned(totalAwarded);
        request.MarkCompleted(totalAwarded, snapshot.PointsUnitCode, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static async Task<T> RunInSystemScope<T>(ApplicationContext scope, Func<Task<T>> action)
    {
        scope.EnterSystemScope();
        try { return await action(); }
        finally { scope.LeaveSystemScope(); }
    }
}
