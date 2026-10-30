using System.Security.Cryptography;
using System.Text.Json;
using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Campaigns.Domain;
using Loyalty.Core.Modules.Cvm.Domain;
using Loyalty.Core.Modules.Integration.Domain;
using Loyalty.Core.Modules.Members.Domain;
using Loyalty.Core.Modules.Notifications.Domain;
using Loyalty.Core.Modules.Raffles.Domain;
using Loyalty.Core.Modules.Rules;
using Loyalty.Core.Modules.Wallets.Application;
using Loyalty.Core.Modules.Wallets.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Raffles.Application;

public sealed record CreateRaffleInput(string Name, string? NameAr, string? Description, string? TriggerEventType, List<CampaignCondition>? Conditions, decimal MinAmount, int EntriesPerEvent, long PrizePoints, string? PrizeDescription, DateTimeOffset? DrawAtUtc);
public sealed record RaffleView(Guid Id, string Name, string? NameAr, string? Description, string TriggerEventType, IReadOnlyList<CampaignCondition> Conditions, decimal MinAmount, int EntriesPerEvent, long PrizePoints, string? PrizeDescription, DateTimeOffset? DrawAtUtc, string Status, Guid? WinnerMemberId, long TotalEntries, int Participants);
public sealed record RaffleEntryRow(Guid MemberId, string ExternalCustomerId, long Entries);
public sealed record RaffleResult(int EntriesGranted);

/// <summary>
/// The prize-draw engine (P15): CRUD, worker-side entry accrual, and the weighted-random draw.
/// </summary>
public sealed class RaffleService(LoyaltyDbContext db, ApplicationContext scope, LedgerService ledger, ILoyaltyRuleEvaluator evaluator, IClock clock)
{
    public async Task<RaffleView> CreateAsync(CreateRaffleInput i, CancellationToken ct)
    {
        var r = Raffle.Create(scope.ApplicationId, i.Name, i.NameAr, i.Description, i.TriggerEventType, i.Conditions,
            i.MinAmount, i.EntriesPerEvent < 1 ? 1 : i.EntriesPerEvent, i.PrizePoints, i.PrizeDescription, i.DrawAtUtc, clock.UtcNow);
        db.Raffles.Add(r);
        await db.SaveChangesAsync(ct);
        return await ToViewAsync(r, ct);
    }

    public async Task<IReadOnlyList<RaffleView>> ListAsync(CancellationToken ct)
    {
        var raffles = await db.Raffles.OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        var views = new List<RaffleView>();
        foreach (var r in raffles) views.Add(await ToViewAsync(r, ct));
        return views;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var r = await db.Raffles.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return false;
        db.RaffleEntries.RemoveRange(db.RaffleEntries.Where(e => e.RaffleId == id));
        db.Raffles.Remove(r);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<RaffleEntryRow>> EntriesAsync(Guid raffleId, CancellationToken ct)
    {
        var tallies = await db.RaffleEntries.Where(e => e.RaffleId == raffleId)
            .GroupBy(e => e.MemberId)
            .Select(g => new { MemberId = g.Key, Entries = g.Sum(x => (long)x.Count) })
            .ToListAsync(ct);
        if (tallies.Count == 0) return [];
        var ids = tallies.Select(t => t.MemberId).ToList();
        var names = await db.Members.Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.ExternalCustomerId, ct);
        return tallies.OrderByDescending(t => t.Entries)
            .Select(t => new RaffleEntryRow(t.MemberId, names.GetValueOrDefault(t.MemberId, ""), t.Entries)).ToList();
    }

    /// <summary>Worker: grant entries to the member for every open raffle this event qualifies for.</summary>
    public async Task<RaffleResult> EvaluateAsync(Wallet wallet, Member member, NormalizedEvent normalized, Guid sourceEventId, DateTimeOffset now, CancellationToken ct)
    {
        var raffles = await db.Raffles
            .Where(r => r.ApplicationId == member.ApplicationId && r.Status == RaffleStatus.Open && r.TriggerEventType == normalized.EventType)
            .ToListAsync(ct);
        if (raffles.Count == 0) return new RaffleResult(0);

        var granted = 0;
        foreach (var r in raffles)
        {
            if (!r.AmountQualifies(normalized.Amount)) continue;
            if (!evaluator.Evaluate(r.Conditions, normalized.Facts).Matched) continue;
            if (await db.RaffleEntries.AnyAsync(e => e.RaffleId == r.Id && e.SourceEventId == sourceEventId, ct)) continue; // idempotent

            db.RaffleEntries.Add(new RaffleEntry(member.ApplicationId, r.Id, member.Id, r.EntriesPerEvent, sourceEventId, now));
            db.EventLog.Add(EventLogEntry.Create(member.ApplicationId, ResultEventTypes.RaffleEntryGranted, member.Id, null,
                JsonSerializer.Serialize(new { memberId = member.Id, raffleId = r.Id, entries = r.EntriesPerEvent }, JsonOptionsProvider.Default), now, now));
            granted += r.EntriesPerEvent;
        }
        return new RaffleResult(granted);
    }

    /// <summary>Pick a weighted-random winner, award the prize points, and close the raffle (idempotent).</summary>
    public async Task<RaffleView?> DrawAsync(Guid raffleId, CancellationToken ct)
    {
        var raffle = await db.Raffles.FirstOrDefaultAsync(r => r.Id == raffleId, ct);
        if (raffle is null) return null;
        if (raffle.Status != RaffleStatus.Open) return await ToViewAsync(raffle, ct); // already drawn/cancelled → return as-is

        var tallies = await (from e in db.RaffleEntries
                             where e.RaffleId == raffleId
                             group e by e.MemberId into g
                             select new { MemberId = g.Key, Weight = g.Sum(x => x.Count) }).ToListAsync(ct);

        var now = clock.UtcNow;
        if (tallies.Count == 0) { raffle.MarkDrawn(null, now); await db.SaveChangesAsync(ct); return await ToViewAsync(raffle, ct); }

        var total = tallies.Sum(t => (long)t.Weight);
        var pick = RandomNumberGenerator.GetInt32(0, (int)Math.Min(total, int.MaxValue));
        long cumulative = 0;
        var winnerId = tallies[^1].MemberId;
        foreach (var t in tallies) { cumulative += t.Weight; if (pick < cumulative) { winnerId = t.MemberId; break; } }

        raffle.MarkDrawn(winnerId, now);

        if (raffle.PrizePoints > 0)
        {
            var winnerWallet = await db.Wallets.FirstAsync(w => w.MemberId == winnerId, ct);
            var profile = await db.ValueProfiles.FirstOrDefaultAsync(p => p.MemberId == winnerId, ct);
            await ledger.PostEarnAsync(winnerWallet, raffle.PrizePoints, Guid.NewGuid(), "system",
                $"raffle-{raffle.Id:N}", $"raffle|{raffle.Id}|prize", $"raffle-{raffle.Id:N}", campaignId: null, $"raffle-prize:{raffle.Id}", now, ct);
            profile?.AddPointsEarned(raffle.PrizePoints);
        }
        db.EventLog.Add(EventLogEntry.Create(raffle.ApplicationId, ResultEventTypes.RaffleWon, winnerId, null,
            JsonSerializer.Serialize(new { raffleId = raffle.Id, name = raffle.Name, winnerMemberId = winnerId, prizePoints = raffle.PrizePoints, prizeDescription = raffle.PrizeDescription }, JsonOptionsProvider.Default), now, now));
        await db.SaveChangesAsync(ct);
        return await ToViewAsync(raffle, ct);
    }

    private async Task<RaffleView> ToViewAsync(Raffle r, CancellationToken ct)
    {
        var total = await db.RaffleEntries.Where(e => e.RaffleId == r.Id).SumAsync(e => (long?)e.Count, ct) ?? 0;
        var participants = await db.RaffleEntries.Where(e => e.RaffleId == r.Id).Select(e => e.MemberId).Distinct().CountAsync(ct);
        return new RaffleView(r.Id, r.Name, r.NameAr, r.Description, r.TriggerEventType, r.Conditions, r.MinAmount,
            r.EntriesPerEvent, r.PrizePoints, r.PrizeDescription, r.DrawAtUtc, r.Status.ToString(), r.WinnerMemberId, total, participants);
    }
}
