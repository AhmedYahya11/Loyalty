using System.Text.Json;
using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Applications.Application;
using Loyalty.Core.Modules.Campaigns.Application;
using Loyalty.Core.Modules.Campaigns.Domain;
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
/// restore application scope from the message, provision the member, select and evaluate
/// campaigns, write balanced ledger postings and the wallet projection, and update request
/// status — all in one database transaction. Redelivery is a no-op via the terminal-status
/// check, the reward-effect unique index, and the ledger-transaction unique index.
///
/// A dedicated DI scope is created so the DbContext and the ApplicationContext it reads for
/// the global query filter are guaranteed to be the same instance.
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
        var clock = sp.GetRequiredService<IClock>();

        // establish the business scope straight from the message (the app is visible to itself)
        var visible = await RunInSystemScope(scope, () => scopeResolver.ResolveVisibleAsync(message.ApplicationId, ct));
        scope.Establish(message.ApplicationId, message.AccountId, sourceSystem: string.Empty, visible);

        var request = await db.IngestionRequests.FirstOrDefaultAsync(r => r.Id == message.RequestId, ct)
            ?? throw new InvalidOperationException($"Ingestion request {message.RequestId} not found.");

        // idempotent no-op on redelivery of an already-finished request
        if (request.Status is RequestStatus.Completed or RequestStatus.Rejected or RequestStatus.Failed)
            return;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        request.MarkProcessing(clock.UtcNow);

        var envelope = JsonSerializer.Deserialize<LoyaltyEventEnvelopeV1>(request.Payload, JsonOptionsProvider.Default)!;
        var normalized = registry.Normalize(envelope, request.SourceSystem);
        var snapshot = JsonSerializer.Deserialize<SettingsSnapshotDto>(request.SettingsSnapshot, JsonOptionsProvider.Default)!;

        // provision member + wallet + ledger accounts (idempotent); persist so account ids resolve
        var (member, wallet) = await provisioning.EnsureAsync(
            request.ApplicationId, envelope.CustomerId, snapshot.PointsUnitCode, ct);
        await db.SaveChangesAsync(ct);

        // select candidate campaigns for this event type
        var candidates = await db.Campaigns
            .Where(c => c.Status == CampaignStatus.Active
                        && c.TriggerEventType == envelope.EventType
                        && c.MinimumEventVersion <= envelope.EventVersion)
            .ToListAsync(ct);
        candidates = candidates.Where(c => c.IsScheduleOpen(envelope.OccurredAt)).ToList();

        var decision = decider.Decide(snapshot.SpendAmountPerPoint, snapshot.RoundingPolicy, normalized, candidates);
        if (!decision.HasReward)
        {
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
