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
public sealed class BirthdaySweepService(IRepository<Loyalty.Core.Modules.Engagement.Domain.EngagementSettings> engagementSettingsRepo, IRepository<Loyalty.Core.Modules.Members.Domain.Member> membersRepo, IRepository<Loyalty.Core.Modules.Integration.Domain.RewardEffect> rewardEffectsRepo, IRepository<Loyalty.Core.Modules.Wallets.Domain.Wallet> walletsRepo, IRepository<Loyalty.Core.Modules.Cvm.Domain.MemberValueProfile> valueProfilesRepo, IRepository<Loyalty.Core.Modules.Notifications.Domain.EventLogEntry> eventLogRepo, IUnitOfWork uow, ApplicationContext scope, LedgerService ledger, IClock clock)
{
    public async Task<int> RunAsync(CancellationToken ct) => await RunForAsync(clock.UtcNow, ct);

    public async Task<int> RunForAsync(DateTimeOffset asOf, CancellationToken ct)
    {
        scope.EnterSystemScope();
        try
        {
            var today = DateOnly.FromDateTime(asOf.UtcDateTime);
            var year = today.Year;
            var appIds = await engagementSettingsRepo.Query().Where(s => s.BirthdayEnabled && s.BirthdayBonusPoints > 0)
                .Select(s => s.ApplicationId).ToListAsync(ct);

            var rewarded = 0;
            foreach (var appId in appIds)
            {
                var settings = await engagementSettingsRepo.Query().FirstAsync(s => s.ApplicationId == appId, ct);
                // small member sets per app in this MVP → filter day/month client-side for provider portability
                var candidates = (await membersRepo.Query().Where(m => m.ApplicationId == appId && m.BirthDate != null).ToListAsync(ct))
                    .Where(m => m.BirthDate!.Value.Month == today.Month && m.BirthDate.Value.Day == today.Day)
                    .ToList();

                foreach (var member in candidates)
                {
                    var effectId = $"birthday:{year}";
                    if (await rewardEffectsRepo.Query().AnyAsync(e => e.MemberId == member.Id && e.EffectId == effectId, ct)) continue;

                    var wallet = await walletsRepo.Query().FirstAsync(w => w.Id == member.DefaultWalletId, ct);
                    var profile = await valueProfilesRepo.Query().FirstAsync(p => p.MemberId == member.Id, ct);
                    var now = clock.UtcNow;
                    var sourceEventId = Guid.NewGuid();

                    var tx = await ledger.PostEarnAsync(wallet, settings.BirthdayBonusPoints, sourceEventId, "system",
                        $"birthday-{year}", $"birthday|{member.Id}|{year}", $"birthday-{year}", campaignId: null, effectId, now, ct);
                    profile.AddPointsEarned(settings.BirthdayBonusPoints);
                    rewardEffectsRepo.Add(new RewardEffect(Guid.NewGuid(), appId, member.Id, sourceEventId, Guid.Empty, 0,
                        effectId, settings.BirthdayBonusPoints, tx.Id, now));
                    eventLogRepo.Add(EventLogEntry.Create(appId, ResultEventTypes.BirthdayRewarded, member.Id, null,
                        JsonSerializer.Serialize(new { memberId = member.Id, bonusPoints = settings.BirthdayBonusPoints, year }, JsonOptionsProvider.Default), now, now));
                    rewarded++;
                }
                await uow.SaveChangesAsync(ct);
            }
            return rewarded;
        }
        finally { scope.LeaveSystemScope(); }
    }
}
