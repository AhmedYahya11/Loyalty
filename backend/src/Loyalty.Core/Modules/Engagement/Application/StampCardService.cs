using System.Text.Json;
using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Campaigns.Domain;
using Loyalty.Core.Modules.Cvm.Domain;
using Loyalty.Core.Modules.Engagement.Domain;
using Loyalty.Core.Modules.Integration.Domain;
using Loyalty.Core.Modules.Members.Domain;
using Loyalty.Core.Modules.Notifications.Domain;
using Loyalty.Core.Modules.Rules;
using Loyalty.Core.Modules.Wallets.Application;
using Loyalty.Core.Modules.Wallets.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Engagement.Application;

public sealed record CreateStampCardInput(string Name, string? NameAr, string? Description, string? TriggerEventType, int StampsRequired, long RewardPoints, List<CampaignCondition>? Conditions = null, DateTimeOffset? StartsAtUtc = null, DateTimeOffset? EndsAtUtc = null);
public sealed record UpdateStampCardInput(string Name, string? NameAr, string? Description, string? TriggerEventType, int StampsRequired, long RewardPoints, bool IsActive, List<CampaignCondition>? Conditions = null, DateTimeOffset? StartsAtUtc = null, DateTimeOffset? EndsAtUtc = null);
public sealed record StampCardView(Guid Id, string Name, string? NameAr, string? Description, string TriggerEventType, int StampsRequired, long RewardPoints, bool IsActive, IReadOnlyList<CampaignCondition> Conditions, DateTimeOffset? StartsAtUtc, DateTimeOffset? EndsAtUtc);
public sealed record StampCardResult(long Bonus, int Completed);

/// <summary>
/// Admin CRUD for stamp cards plus the worker-side evaluation (P10). <see cref="EvaluateAsync"/> is
/// called from the worker on each event: it adds a stamp to every active card whose trigger matches,
/// and when a card fills it awards the reward points through the ledger and resets (keeping the
/// remainder). Operates on tracked entities in the worker's transaction; does not commit.
/// </summary>
public sealed class StampCardService(
    IRepository<Loyalty.Core.Modules.Engagement.Domain.StampCard> stampCardsRepo,
    IRepository<Loyalty.Core.Modules.Engagement.Domain.MemberStampCard> memberStampCardsRepo,
    IRepository<Loyalty.Core.Modules.Integration.Domain.RewardEffect> rewardEffectsRepo,
    IRepository<Loyalty.Core.Modules.Notifications.Domain.EventLogEntry> eventLogRepo,
    IUnitOfWork uow, ApplicationContext scope, LedgerService ledger, ILoyaltyRuleEvaluator evaluator, IClock clock)
{
    public async Task<StampCardView> CreateAsync(CreateStampCardInput i, CancellationToken ct)
    {
        var c = StampCard.Create(scope.ApplicationId, i.Name, i.NameAr, i.Description, i.TriggerEventType, i.StampsRequired, i.RewardPoints, clock.UtcNow, i.Conditions, i.StartsAtUtc, i.EndsAtUtc);
        stampCardsRepo.Add(c);
        await uow.SaveChangesAsync(ct);
        return ToView(c);
    }

    public async Task<StampCardView?> UpdateAsync(Guid id, UpdateStampCardInput i, CancellationToken ct)
    {
        var c = await stampCardsRepo.Query().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return null;
        c.Update(i.Name, i.NameAr, i.Description, i.TriggerEventType, i.StampsRequired, i.RewardPoints, i.IsActive, clock.UtcNow, i.Conditions, i.StartsAtUtc, i.EndsAtUtc);
        await uow.SaveChangesAsync(ct);
        return ToView(c);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var c = await stampCardsRepo.Query().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return false;
        stampCardsRepo.Remove(c);
        await uow.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<StampCardView>> ListAsync(CancellationToken ct) =>
        (await stampCardsRepo.Query().OrderBy(c => c.Name).ToListAsync(ct)).Select(ToView).ToList();

    /// <summary>A member's progress across all stamp cards (for the customer/member view).</summary>
    public async Task<IReadOnlyList<object>> ProgressAsync(Guid memberId, CancellationToken ct) =>
        await (
            from mc in memberStampCardsRepo.Query()
            join c in stampCardsRepo.Query() on mc.StampCardId equals c.Id
            where mc.MemberId == memberId
            orderby c.Name
            select (object)new { stampCardId = c.Id, name = c.Name, nameAr = c.NameAr, stamps = mc.Stamps, stampsRequired = c.StampsRequired, completedCount = mc.CompletedCount, rewardPoints = c.RewardPoints }
        ).ToListAsync(ct);

    public async Task<StampCardResult> EvaluateAsync(
        Wallet wallet, Member member, MemberValueProfile profile, NormalizedEvent normalized,
        Guid sourceEventId, string sourceSystem, string sourceTransactionId, string businessEventKey, string correlationId,
        DateTimeOffset occurredAt, DateTimeOffset now, CancellationToken ct)
    {
        var cards = await stampCardsRepo.Query()
            .Where(c => c.ApplicationId == member.ApplicationId && c.IsActive && c.TriggerEventType == normalized.EventType)
            .ToListAsync(ct);
        // P13: only cards whose validity window is open AND whose attribute conditions match this event
        cards = cards.Where(c => c.IsScheduleOpen(occurredAt) && evaluator.Evaluate(c.Conditions, normalized.Facts).Matched).ToList();
        if (cards.Count == 0) return new StampCardResult(0, 0);

        long bonus = 0; var completed = 0;
        foreach (var card in cards)
        {
            var progress = await memberStampCardsRepo.Query().FirstOrDefaultAsync(p => p.MemberId == member.Id && p.StampCardId == card.Id, ct);
            if (progress is null) { progress = new MemberStampCard(member.ApplicationId, member.Id, card.Id, now); memberStampCardsRepo.Add(progress); }

            // one stamp per source event (idempotent): skip if this event already stamped this card
            var effectId = $"stamp:{card.Id}";
            if (await rewardEffectsRepo.Query().AnyAsync(e => e.MemberId == member.Id && e.SourceEventId == sourceEventId && e.EffectId == effectId, ct))
                continue;

            var cardCompleted = progress.AddStampAndCheckComplete(card.StampsRequired, now);
            var ledgerTxId = Guid.Empty;
            if (cardCompleted && card.RewardPoints > 0)
            {
                var tx = await ledger.PostEarnAsync(wallet, card.RewardPoints, sourceEventId, sourceSystem, sourceTransactionId,
                    businessEventKey, correlationId, campaignId: null, $"stampcard-reward:{card.Id}", occurredAt, ct);
                ledgerTxId = tx.Id;
                profile.AddPointsEarned(card.RewardPoints);
                bonus += card.RewardPoints;
            }
            rewardEffectsRepo.Add(new Modules.Integration.Domain.RewardEffect(Guid.NewGuid(), member.ApplicationId, member.Id,
                sourceEventId, Guid.Empty, 0, effectId, cardCompleted ? card.RewardPoints : 0, ledgerTxId, now));
            if (cardCompleted)
            {
                completed++;
                eventLogRepo.Add(EventLogEntry.Create(member.ApplicationId, ResultEventTypes.StampCardCompleted, member.Id, null,
                    JsonSerializer.Serialize(new { memberId = member.Id, stampCardId = card.Id, name = card.Name, rewardPoints = card.RewardPoints }, JsonOptionsProvider.Default), now, now));
            }
        }
        return new StampCardResult(bonus, completed);
    }

    /// <summary>
    /// Reverse the stamps a now-refunded purchase earned (P14). For each original purchase event, undo
    /// its stamp: if the event had completed a card, decrement the completion count; otherwise remove
    /// the stamp. The reward POINTS of a completion are ledgered under the purchase's business key, so
    /// they are already undone by the caller's earn reversal — this method only corrects the stamp
    /// counters. Idempotent via a <c>stamp-reversal:{card}</c> reward-effect keyed on the original event.
    /// Returns the number of stamp cards adjusted.
    /// </summary>
    public async Task<int> ReverseForEventsAsync(
        Wallet wallet, Member member, MemberValueProfile profile, IReadOnlyCollection<Guid> originalEventIds,
        Guid refundEventId, DateTimeOffset now, CancellationToken ct)
    {
        if (originalEventIds.Count == 0) return 0;
        var adjusted = 0;

        var stampEffects = await rewardEffectsRepo.Query()
            .Where(e => e.MemberId == member.Id && originalEventIds.Contains(e.SourceEventId) && e.EffectId.StartsWith("stamp:"))
            .ToListAsync(ct);

        foreach (var effect in stampEffects)
        {
            var cardId = Guid.Parse(effect.EffectId["stamp:".Length..]);
            var reversalId = $"stamp-reversal:{cardId}";
            if (await rewardEffectsRepo.Query().AnyAsync(e => e.MemberId == member.Id && e.SourceEventId == effect.SourceEventId && e.EffectId == reversalId, ct))
                continue; // already reversed this event's stamp

            var progress = await memberStampCardsRepo.Query().FirstOrDefaultAsync(p => p.MemberId == member.Id && p.StampCardId == cardId, ct);
            if (progress is null) continue;

            if (effect.Points > 0) progress.ReverseCompletion(now); // reward points already reversed by the earn reversal
            else progress.RemoveStamp(now);

            rewardEffectsRepo.Add(new Modules.Integration.Domain.RewardEffect(Guid.NewGuid(), member.ApplicationId, member.Id,
                effect.SourceEventId, Guid.Empty, 0, reversalId, 0, Guid.Empty, now));
            eventLogRepo.Add(EventLogEntry.Create(member.ApplicationId, ResultEventTypes.StampCardReversed, member.Id, null,
                JsonSerializer.Serialize(new { memberId = member.Id, stampCardId = cardId, completionReversed = effect.Points > 0 }, JsonOptionsProvider.Default), now, now));
            adjusted++;
        }
        return adjusted;
    }

    private static StampCardView ToView(StampCard c) =>
        new(c.Id, c.Name, c.NameAr, c.Description, c.TriggerEventType, c.StampsRequired, c.RewardPoints, c.IsActive, c.Conditions, c.StartsAtUtc, c.EndsAtUtc);
}
