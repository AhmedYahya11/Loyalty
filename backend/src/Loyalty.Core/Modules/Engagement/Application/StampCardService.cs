using System.Text.Json;
using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Cvm.Domain;
using Loyalty.Core.Modules.Engagement.Domain;
using Loyalty.Core.Modules.Members.Domain;
using Loyalty.Core.Modules.Notifications.Domain;
using Loyalty.Core.Modules.Wallets.Application;
using Loyalty.Core.Modules.Wallets.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Engagement.Application;

public sealed record CreateStampCardInput(string Name, string? NameAr, string? Description, string? TriggerEventType, int StampsRequired, long RewardPoints);
public sealed record UpdateStampCardInput(string Name, string? NameAr, string? Description, string? TriggerEventType, int StampsRequired, long RewardPoints, bool IsActive);
public sealed record StampCardView(Guid Id, string Name, string? NameAr, string? Description, string TriggerEventType, int StampsRequired, long RewardPoints, bool IsActive);
public sealed record StampCardResult(long Bonus, int Completed);

/// <summary>
/// Admin CRUD for stamp cards plus the worker-side evaluation (P10). <see cref="EvaluateAsync"/> is
/// called from the worker on each event: it adds a stamp to every active card whose trigger matches,
/// and when a card fills it awards the reward points through the ledger and resets (keeping the
/// remainder). Operates on tracked entities in the worker's transaction; does not commit.
/// </summary>
public sealed class StampCardService(LoyaltyDbContext db, ApplicationContext scope, LedgerService ledger, IClock clock)
{
    public async Task<StampCardView> CreateAsync(CreateStampCardInput i, CancellationToken ct)
    {
        var c = StampCard.Create(scope.ApplicationId, i.Name, i.NameAr, i.Description, i.TriggerEventType, i.StampsRequired, i.RewardPoints, clock.UtcNow);
        db.StampCards.Add(c);
        await db.SaveChangesAsync(ct);
        return ToView(c);
    }

    public async Task<StampCardView?> UpdateAsync(Guid id, UpdateStampCardInput i, CancellationToken ct)
    {
        var c = await db.StampCards.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return null;
        c.Update(i.Name, i.NameAr, i.Description, i.TriggerEventType, i.StampsRequired, i.RewardPoints, i.IsActive, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return ToView(c);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var c = await db.StampCards.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return false;
        db.StampCards.Remove(c);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<StampCardView>> ListAsync(CancellationToken ct) =>
        (await db.StampCards.OrderBy(c => c.Name).ToListAsync(ct)).Select(ToView).ToList();

    /// <summary>A member's progress across all stamp cards (for the customer/member view).</summary>
    public async Task<IReadOnlyList<object>> ProgressAsync(Guid memberId, CancellationToken ct) =>
        await (
            from mc in db.MemberStampCards
            join c in db.StampCards on mc.StampCardId equals c.Id
            where mc.MemberId == memberId
            orderby c.Name
            select (object)new { stampCardId = c.Id, name = c.Name, nameAr = c.NameAr, stamps = mc.Stamps, stampsRequired = c.StampsRequired, completedCount = mc.CompletedCount, rewardPoints = c.RewardPoints }
        ).ToListAsync(ct);

    public async Task<StampCardResult> EvaluateAsync(
        Wallet wallet, Member member, MemberValueProfile profile, string eventType,
        Guid sourceEventId, string sourceSystem, string sourceTransactionId, string businessEventKey, string correlationId,
        DateTimeOffset occurredAt, DateTimeOffset now, CancellationToken ct)
    {
        var cards = await db.StampCards
            .Where(c => c.ApplicationId == member.ApplicationId && c.IsActive && c.TriggerEventType == eventType)
            .ToListAsync(ct);
        if (cards.Count == 0) return new StampCardResult(0, 0);

        long bonus = 0; var completed = 0;
        foreach (var card in cards)
        {
            var progress = await db.MemberStampCards.FirstOrDefaultAsync(p => p.MemberId == member.Id && p.StampCardId == card.Id, ct);
            if (progress is null) { progress = new MemberStampCard(member.ApplicationId, member.Id, card.Id, now); db.MemberStampCards.Add(progress); }

            // one stamp per source event (idempotent): skip if this event already stamped this card
            var effectId = $"stamp:{card.Id}";
            if (await db.RewardEffects.AnyAsync(e => e.MemberId == member.Id && e.SourceEventId == sourceEventId && e.EffectId == effectId, ct))
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
            db.RewardEffects.Add(new Modules.Integration.Domain.RewardEffect(Guid.NewGuid(), member.ApplicationId, member.Id,
                sourceEventId, Guid.Empty, 0, effectId, cardCompleted ? card.RewardPoints : 0, ledgerTxId, now));
            if (cardCompleted)
            {
                completed++;
                db.EventLog.Add(EventLogEntry.Create(member.ApplicationId, ResultEventTypes.StampCardCompleted, member.Id, null,
                    JsonSerializer.Serialize(new { memberId = member.Id, stampCardId = card.Id, name = card.Name, rewardPoints = card.RewardPoints }, JsonOptionsProvider.Default), now, now));
            }
        }
        return new StampCardResult(bonus, completed);
    }

    private static StampCardView ToView(StampCard c) =>
        new(c.Id, c.Name, c.NameAr, c.Description, c.TriggerEventType, c.StampsRequired, c.RewardPoints, c.IsActive);
}
