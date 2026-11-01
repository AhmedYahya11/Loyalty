using System.Text.Json;
using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Cvm.Domain;
using Loyalty.Core.Modules.Gamification.Domain;
using Loyalty.Core.Modules.Members.Domain;
using Loyalty.Core.Modules.Notifications.Domain;
using Loyalty.Core.Modules.Wallets.Application;
using Loyalty.Core.Modules.Wallets.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Gamification.Application;

/// <summary>
/// Evaluates gamification after an event (MVP spec future "Achievements"): unlocks metric-based
/// achievements once, advances time-boxed challenges, awards bonus points for each, and emits
/// result events. Operates on tracked entities inside the worker's transaction; returns the total
/// bonus points awarded so the caller can roll it into the request result + profile.
/// </summary>
public sealed record GamificationResult(long Bonus, int Unlocked, int ChallengesCompleted)
{
    public bool Any => Bonus > 0 || Unlocked > 0 || ChallengesCompleted > 0;
}

public sealed class GamificationService(IRepository<Loyalty.Core.Modules.Gamification.Domain.Achievement> achievementsRepo, IRepository<Loyalty.Core.Modules.Gamification.Domain.MemberAchievement> memberAchievementsRepo, IRepository<Loyalty.Core.Modules.Gamification.Domain.Challenge> challengesRepo, IRepository<Loyalty.Core.Modules.Gamification.Domain.MemberChallengeProgress> challengeProgressRepo, IRepository<Loyalty.Core.Modules.Notifications.Domain.EventLogEntry> eventLogRepo, LedgerService ledger, IClock clock)
{
    public async Task<GamificationResult> EvaluateAsync(
        Wallet wallet, Member member, MemberValueProfile profile, string eventType, decimal? amount,
        Guid sourceEventId, string sourceSystem, string sourceTransactionId, string businessEventKey, string correlationId,
        DateTimeOffset occurredAt, DateTimeOffset now, CancellationToken ct)
    {
        long bonus = 0;
        var unlocked = 0;
        var challengesCompleted = 0;
        var metrics = new GamificationMetrics(profile.PurchaseCount, profile.NetSpendAmount, profile.PointsEarned,
            profile.LoginCount, profile.ReferralCount, profile.CurrentStreak);

        // --- achievements (one-time unlock) ---
        var achievements = await achievementsRepo.Query().Where(a => a.ApplicationId == member.ApplicationId && a.IsActive).ToListAsync(ct);
        if (achievements.Count > 0)
        {
            var unlockedIds = await memberAchievementsRepo.Query()
                .Where(ma => ma.MemberId == member.Id).Select(ma => ma.AchievementId).ToListAsync(ct);
            foreach (var a in achievements.Where(a => !unlockedIds.Contains(a.Id) && a.Unlocked(metrics)))
            {
                memberAchievementsRepo.Add(new MemberAchievement(member.ApplicationId, member.Id, a.Id, now));
                bonus += await AwardBonusAsync(wallet, profile, a.BonusPoints, $"achievement:{a.Id}",
                    sourceEventId, sourceSystem, sourceTransactionId, businessEventKey, correlationId, occurredAt, now, ct);
                unlocked++;
                EmitEvent(member.ApplicationId, ResultEventTypes.AchievementUnlocked, member.Id,
                    new { memberId = member.Id, achievementId = a.Id, name = a.Name, bonusPoints = a.BonusPoints }, now);
            }
        }

        // --- challenges (windowed progress) ---
        var challenges = await challengesRepo.Query().Where(c => c.ApplicationId == member.ApplicationId && c.IsActive).ToListAsync(ct);
        foreach (var c in challenges.Where(c => c.IsOpen(occurredAt)))
        {
            var contribution = c.Contribution(eventType, amount);
            if (contribution <= 0) continue;

            var progress = await challengeProgressRepo.Query().FirstOrDefaultAsync(p => p.MemberId == member.Id && p.ChallengeId == c.Id, ct);
            if (progress is null) { progress = new MemberChallengeProgress(member.ApplicationId, member.Id, c.Id, now); challengeProgressRepo.Add(progress); }
            if (progress.Completed) continue;

            progress.Add(contribution, now);
            if (progress.Progress >= c.TargetValue)
            {
                progress.Complete(now);
                bonus += await AwardBonusAsync(wallet, profile, c.BonusPoints, $"challenge:{c.Id}",
                    sourceEventId, sourceSystem, sourceTransactionId, businessEventKey, correlationId, occurredAt, now, ct);
                challengesCompleted++;
                EmitEvent(member.ApplicationId, ResultEventTypes.ChallengeCompleted, member.Id,
                    new { memberId = member.Id, challengeId = c.Id, name = c.Name, bonusPoints = c.BonusPoints }, now);
            }
        }

        return new GamificationResult(bonus, unlocked, challengesCompleted);
    }

    /// <summary>
    /// Reduce open challenge progress when a contributing purchase is refunded (P14). Uses the same
    /// <see cref="Challenge.Contribution"/> rule against the refunded amount, so a refund subtracts
    /// exactly what the purchase added. Already-completed challenges keep their bonus (like achievements).
    /// </summary>
    public async Task<int> ReverseChallengesAsync(Member member, decimal? refundAmount, DateTimeOffset occurredAt, DateTimeOffset now, CancellationToken ct)
    {
        var challenges = await challengesRepo.Query().Where(c => c.ApplicationId == member.ApplicationId).ToListAsync(ct);
        var reversed = 0;
        foreach (var c in challenges)
        {
            var contribution = c.Contribution(Integration.Contracts.EventTypes.PurchaseCompleted, refundAmount);
            if (contribution <= 0) continue;
            var progress = await challengeProgressRepo.Query().FirstOrDefaultAsync(p => p.MemberId == member.Id && p.ChallengeId == c.Id, ct);
            if (progress is null || progress.Completed || progress.Progress <= 0) continue;

            progress.Subtract(contribution, now);
            reversed++;
            EmitEvent(member.ApplicationId, ResultEventTypes.ChallengeProgressReversed, member.Id,
                new { memberId = member.Id, challengeId = c.Id, name = c.Name, reversed = contribution }, now);
        }
        return reversed;
    }

    private async Task<long> AwardBonusAsync(Wallet wallet, MemberValueProfile profile, long points, string effectId,
        Guid sourceEventId, string sourceSystem, string sourceTransactionId, string businessEventKey, string correlationId,
        DateTimeOffset occurredAt, DateTimeOffset now, CancellationToken ct)
    {
        if (points <= 0) return 0;
        await ledger.PostEarnAsync(wallet, points, sourceEventId, sourceSystem, sourceTransactionId, businessEventKey,
            correlationId, campaignId: null, effectId, occurredAt, ct);
        profile.AddPointsEarned(points);
        return points;
    }

    private void EmitEvent(Guid applicationId, string eventType, Guid memberId, object payload, DateTimeOffset now) =>
        eventLogRepo.Add(EventLogEntry.Create(applicationId, eventType, memberId, null,
            JsonSerializer.Serialize(payload, JsonOptionsProvider.Default), now, now));
}
