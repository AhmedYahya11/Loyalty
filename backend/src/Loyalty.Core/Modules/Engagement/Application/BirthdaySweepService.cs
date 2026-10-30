using System.Text.Json;
using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Integration.Domain;
using Loyalty.Core.Modules.Notifications.Domain;
using Loyalty.Core.Modules.Wallets.Application;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Engagement.Application;

/// <summary>
/// The daily scheduled birthday reward (P10). For every application with the birthday program
/// enabled, members whose birthday (month+day) is "today" are awarded the configured bonus once per
/// calendar year — idempotent via a <c>birthday:{year}</c> reward-effect, so reruns and restarts are
/// safe. Runs in system scope; a captured timestamp defines "today". Mirrors the lifecycle sweep.
/// </summary>
public sealed class BirthdaySweepService(LoyaltyDbContext db, ApplicationContext scope, LedgerService ledger, IClock clock)
{
    public async Task<int> RunAsync(CancellationToken ct) => await RunForAsync(clock.UtcNow, ct);

    public async Task<int> RunForAsync(DateTimeOffset asOf, CancellationToken ct)
    {
        scope.EnterSystemScope();
        try
        {
            var today = DateOnly.FromDateTime(asOf.UtcDateTime);
            var year = today.Year;
            var appIds = await db.EngagementSettings.Where(s => s.BirthdayEnabled && s.BirthdayBonusPoints > 0)
                .Select(s => s.ApplicationId).ToListAsync(ct);

            var rewarded = 0;
            foreach (var appId in appIds)
            {
                var settings = await db.EngagementSettings.FirstAsync(s => s.ApplicationId == appId, ct);
                // small member sets per app in this MVP → filter day/month client-side for provider portability
                var candidates = (await db.Members.Where(m => m.ApplicationId == appId && m.BirthDate != null).ToListAsync(ct))
                    .Where(m => m.BirthDate!.Value.Month == today.Month && m.BirthDate.Value.Day == today.Day)
                    .ToList();

                foreach (var member in candidates)
                {
                    var effectId = $"birthday:{year}";
                    if (await db.RewardEffects.AnyAsync(e => e.MemberId == member.Id && e.EffectId == effectId, ct)) continue;

                    var wallet = await db.Wallets.FirstAsync(w => w.Id == member.DefaultWalletId, ct);
                    var profile = await db.ValueProfiles.FirstAsync(p => p.MemberId == member.Id, ct);
                    var now = clock.UtcNow;
                    var sourceEventId = Guid.NewGuid();

                    var tx = await ledger.PostEarnAsync(wallet, settings.BirthdayBonusPoints, sourceEventId, "system",
                        $"birthday-{year}", $"birthday|{member.Id}|{year}", $"birthday-{year}", campaignId: null, effectId, now, ct);
                    profile.AddPointsEarned(settings.BirthdayBonusPoints);
                    db.RewardEffects.Add(new RewardEffect(Guid.NewGuid(), appId, member.Id, sourceEventId, Guid.Empty, 0,
                        effectId, settings.BirthdayBonusPoints, tx.Id, now));
                    db.EventLog.Add(EventLogEntry.Create(appId, ResultEventTypes.BirthdayRewarded, member.Id, null,
                        JsonSerializer.Serialize(new { memberId = member.Id, bonusPoints = settings.BirthdayBonusPoints, year }, JsonOptionsProvider.Default), now, now));
                    rewarded++;
                }
                await db.SaveChangesAsync(ct);
            }
            return rewarded;
        }
        finally { scope.LeaveSystemScope(); }
    }
}
