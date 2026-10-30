using System.Text.Json;
using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Cvm.Domain;
using Loyalty.Core.Modules.Engagement.Domain;
using Loyalty.Core.Modules.Integration.Domain;
using Loyalty.Core.Modules.Members.Domain;
using Loyalty.Core.Modules.Notifications.Domain;
using Loyalty.Core.Modules.Wallets.Application;
using Loyalty.Core.Modules.Wallets.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Engagement.Application;

public sealed record ReferralResult(long RefereeBonus, long ReferrerBonus, bool Rewarded);

/// <summary>
/// Rewards both sides of a completed referral (P10). Called from the worker for ReferralCompleted
/// events: resolves the referrer from <c>attributes.referrerCode</c> (preferred) or
/// <c>attributes.referrerCustomerId</c>, pays the referrer + referee out of the application's
/// referral program, and records a <see cref="Referral"/> row (UNIQUE by referralId) so redelivery
/// never double-rewards. Operates on tracked entities in the worker's transaction; does not commit.
/// </summary>
public sealed class ReferralService(LoyaltyDbContext db, LedgerService ledger)
{
    public async Task<ReferralResult> EvaluateAsync(
        Wallet refereeWallet, Member refereeMember, MemberValueProfile refereeProfile, NormalizedEvent normalized,
        Guid sourceEventId, string sourceSystem, string sourceTransactionId, string businessEventKey, string correlationId,
        DateTimeOffset occurredAt, DateTimeOffset now, CancellationToken ct)
    {
        var settings = await db.EngagementSettings.FirstOrDefaultAsync(s => s.ApplicationId == refereeMember.ApplicationId, ct);
        if (settings is null || !settings.ReferralEnabled) return new ReferralResult(0, 0, false);

        var referralKey = Fact(normalized, "attributes.referralId");
        if (string.IsNullOrWhiteSpace(referralKey)) return new ReferralResult(0, 0, false);

        // idempotency: a referral is rewarded at most once per application
        if (await db.Referrals.AnyAsync(r => r.ApplicationId == refereeMember.ApplicationId && r.ReferralKey == referralKey, ct))
            return new ReferralResult(0, 0, false);

        var referrer = await ResolveReferrerAsync(refereeMember.ApplicationId, normalized, ct);
        if (referrer is null || referrer.Id == refereeMember.Id) return new ReferralResult(0, 0, false); // unknown / self-referral

        var referrerWallet = await db.Wallets.FirstAsync(w => w.Id == referrer.DefaultWalletId, ct);
        var referrerProfile = await db.ValueProfiles.FirstAsync(p => p.MemberId == referrer.Id, ct);

        long refereeBonus = await AwardAsync(refereeWallet, refereeProfile, settings.RefereePoints, "referral-referee",
            sourceEventId, sourceSystem, sourceTransactionId, businessEventKey, correlationId, occurredAt, ct);
        long referrerBonus = await AwardAsync(referrerWallet, referrerProfile, settings.ReferrerPoints, "referral-referrer",
            sourceEventId, sourceSystem, sourceTransactionId, businessEventKey, correlationId, occurredAt, ct);

        referrerProfile.ApplyReferral(occurredAt); // the referrer gets the referral credit

        db.Referrals.Add(new Referral(refereeMember.ApplicationId, referralKey!, referrer.Id, refereeMember.Id,
            referrerBonus, refereeBonus, now));

        EmitEvent(refereeMember.ApplicationId, referrer.Id, new
        {
            referralId = referralKey, referrerMemberId = referrer.Id, refereeMemberId = refereeMember.Id,
            referrerPoints = referrerBonus, refereePoints = refereeBonus,
        }, now);

        return new ReferralResult(refereeBonus, referrerBonus, true);
    }

    private async Task<Member?> ResolveReferrerAsync(Guid applicationId, NormalizedEvent normalized, CancellationToken ct)
    {
        var code = Fact(normalized, "attributes.referrerCode");
        if (!string.IsNullOrWhiteSpace(code))
        {
            var byCode = await db.Members.FirstOrDefaultAsync(m => m.ApplicationId == applicationId && m.ReferralCode == code, ct);
            if (byCode is not null) return byCode;
        }
        var ext = Fact(normalized, "attributes.referrerCustomerId");
        if (!string.IsNullOrWhiteSpace(ext))
            return await db.Members.FirstOrDefaultAsync(m => m.ApplicationId == applicationId && m.ExternalCustomerId == ext, ct);
        return null;
    }

    private async Task<long> AwardAsync(Wallet wallet, MemberValueProfile profile, long points, string effectId,
        Guid sourceEventId, string sourceSystem, string sourceTransactionId, string businessEventKey, string correlationId,
        DateTimeOffset occurredAt, CancellationToken ct)
    {
        if (points <= 0) return 0;
        await ledger.PostEarnAsync(wallet, points, sourceEventId, sourceSystem, sourceTransactionId, businessEventKey,
            correlationId, campaignId: null, effectId, occurredAt, ct);
        profile.AddPointsEarned(points);
        return points;
    }

    private static string? Fact(NormalizedEvent n, string key) =>
        n.Facts.TryGetValue(key, out var v) ? v?.ToString() : null;

    private void EmitEvent(Guid applicationId, Guid memberId, object payload, DateTimeOffset now) =>
        db.EventLog.Add(EventLogEntry.Create(applicationId, ResultEventTypes.ReferralRewarded, memberId, null,
            JsonSerializer.Serialize(payload, JsonOptionsProvider.Default), now, now));
}
