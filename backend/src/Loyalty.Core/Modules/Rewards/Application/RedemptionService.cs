using System.Security.Cryptography;
using System.Text.Json;
using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Notifications.Domain;
using Loyalty.Core.Modules.Rewards.Domain;
using Loyalty.Core.Modules.Wallets.Application;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Rewards.Application;

public sealed record RedemptionView(Guid Id, Guid MemberId, Guid RewardId, string RewardName, long PointsCost, string Status, string CouponCode, DateTimeOffset ReservedAt, DateTimeOffset ExpiresAt);

/// <summary>
/// Points redemption following reserve→confirm→cancel (MVP spec §redemption). Reserve holds the
/// points (active→reserved) + inventory and issues a coupon code (the QR payload); confirm spends
/// them (reserved→redemption clearing) and records PointsRedeemed; cancel/expire releases them.
/// Each step commits one balanced ledger transaction in its own DB transaction.
/// </summary>
public sealed class RedemptionService(LoyaltyDbContext db, ApplicationContext scope, LedgerService ledger, IClock clock)
{
    private const string SourceSystem = "redemption";
    private static readonly TimeSpan Hold = TimeSpan.FromHours(24);

    public async Task<RedemptionView> ReserveAsync(Guid memberId, Guid rewardId, CancellationToken ct)
    {
        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == memberId, ct)
            ?? throw new LoyaltyException(LoyaltyError.NotFound("Member"));
        var reward = await db.Rewards.FirstOrDefaultAsync(r => r.Id == rewardId && r.IsActive, ct)
            ?? throw new LoyaltyException(LoyaltyError.NotFound("Reward"));
        var wallet = await db.Wallets.FirstAsync(w => w.Id == member.DefaultWalletId, ct);

        if (wallet.ActiveBalance < reward.PointsCost)
            throw new LoyaltyException(LoyaltyError.Conflict("insufficient_points", "Member does not have enough active points."));

        if (reward.PerMemberLimit is { } limit)
        {
            var used = await db.Redemptions.CountAsync(r => r.MemberId == memberId && r.RewardId == rewardId
                && (r.Status == RedemptionStatus.Reserved || r.Status == RedemptionStatus.Confirmed), ct);
            if (used >= limit)
                throw new LoyaltyException(LoyaltyError.Conflict("per_member_limit_reached", "Per-member redemption limit reached for this reward."));
        }

        if (!reward.TryHoldInventory())
            throw new LoyaltyException(LoyaltyError.Conflict("out_of_stock", "Reward is out of stock."));

        var now = clock.UtcNow;
        var redemption = Redemption.Reserve(scope.ApplicationId, memberId, rewardId, reward.PointsCost, NewCouponCode(), now, Hold);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ledger.PostReserveAsync(wallet, reward.PointsCost, redemption.Id, SourceSystem, redemption.Id.ToString(),
            $"{SourceSystem}|{redemption.Id}", redemption.Id.ToString(), "reserve", now, ct);
        db.Redemptions.Add(redemption);
        EmitEvent(ResultEventTypes.RedemptionReserved, memberId, new { redemptionId = redemption.Id, rewardId, pointsCost = reward.PointsCost, couponCode = redemption.CouponCode }, now);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ToView(redemption, reward.Name);
    }

    public Task<RedemptionView?> ConfirmAsync(Guid redemptionId, CancellationToken ct) =>
        ConfirmInternal(r => r.Id == redemptionId, ct);

    public Task<RedemptionView?> ConfirmByCodeAsync(string couponCode, CancellationToken ct) =>
        ConfirmInternal(r => r.CouponCode == couponCode, ct);

    private async Task<RedemptionView?> ConfirmInternal(System.Linq.Expressions.Expression<Func<Redemption, bool>> match, CancellationToken ct)
    {
        var redemption = await db.Redemptions.FirstOrDefaultAsync(match, ct);
        if (redemption is null) return null;
        if (!redemption.IsHeld)
            throw new LoyaltyException(LoyaltyError.Conflict("redemption_not_reserved", $"Redemption is {redemption.Status}, not Reserved."));

        var member = await db.Members.FirstAsync(m => m.Id == redemption.MemberId, ct);
        var wallet = await db.Wallets.FirstAsync(w => w.Id == member.DefaultWalletId, ct);
        var profile = await db.ValueProfiles.FirstAsync(p => p.MemberId == redemption.MemberId, ct);
        var reward = await db.Rewards.FirstAsync(r => r.Id == redemption.RewardId, ct);
        var now = clock.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ledger.PostRedeemAsync(wallet, redemption.PointsCost, redemption.Id, SourceSystem, redemption.Id.ToString(),
            $"{SourceSystem}|{redemption.Id}", redemption.Id.ToString(), "redeem", now, ct);
        redemption.Confirm(now);
        profile.AddPointsRedeemed(redemption.PointsCost);
        EmitEvent(ResultEventTypes.PointsRedeemed, redemption.MemberId, new { redemptionId = redemption.Id, redemption.RewardId, pointsCost = redemption.PointsCost }, now);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ToView(redemption, reward.Name);
    }

    public async Task<RedemptionView?> CancelAsync(Guid redemptionId, CancellationToken ct)
    {
        var redemption = await db.Redemptions.FirstOrDefaultAsync(r => r.Id == redemptionId, ct);
        if (redemption is null) return null;
        if (!redemption.IsHeld)
            throw new LoyaltyException(LoyaltyError.Conflict("redemption_not_reserved", $"Redemption is {redemption.Status}, not Reserved."));

        var member = await db.Members.FirstAsync(m => m.Id == redemption.MemberId, ct);
        var wallet = await db.Wallets.FirstAsync(w => w.Id == member.DefaultWalletId, ct);
        var reward = await db.Rewards.FirstAsync(r => r.Id == redemption.RewardId, ct);
        var now = clock.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ledger.PostReleaseAsync(wallet, redemption.PointsCost, redemption.Id, SourceSystem, redemption.Id.ToString(),
            $"{SourceSystem}|{redemption.Id}", redemption.Id.ToString(), "release", now, ct);
        redemption.Cancel(now);
        reward.RestoreInventory();
        EmitEvent(ResultEventTypes.RedemptionCancelled, redemption.MemberId, new { redemptionId = redemption.Id, redemption.RewardId, pointsCost = redemption.PointsCost }, now);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ToView(redemption, reward.Name);
    }

    public async Task<IReadOnlyList<RedemptionView>> ListMemberAsync(Guid memberId, CancellationToken ct) =>
        await (from r in db.Redemptions
               join rw in db.Rewards on r.RewardId equals rw.Id
               where r.MemberId == memberId
               orderby r.ReservedAt descending
               select new RedemptionView(r.Id, r.MemberId, r.RewardId, rw.Name, r.PointsCost, r.Status.ToString(), r.CouponCode, r.ReservedAt, r.ExpiresAt)).ToListAsync(ct);

    public async Task<RedemptionView?> GetByCodeAsync(string couponCode, CancellationToken ct) =>
        await (from r in db.Redemptions
               join rw in db.Rewards on r.RewardId equals rw.Id
               where r.CouponCode == couponCode
               select new RedemptionView(r.Id, r.MemberId, r.RewardId, rw.Name, r.PointsCost, r.Status.ToString(), r.CouponCode, r.ReservedAt, r.ExpiresAt)).FirstOrDefaultAsync(ct);

    private void EmitEvent(string eventType, Guid memberId, object payload, DateTimeOffset now) =>
        db.EventLog.Add(EventLogEntry.Create(scope.ApplicationId, eventType, memberId, null,
            JsonSerializer.Serialize(payload, JsonOptionsProvider.Default), now, now));

    private static RedemptionView ToView(Redemption r, string rewardName) =>
        new(r.Id, r.MemberId, r.RewardId, rewardName, r.PointsCost, r.Status.ToString(), r.CouponCode, r.ReservedAt, r.ExpiresAt);

    private static string NewCouponCode()
    {
        Span<byte> b = stackalloc byte[6];
        RandomNumberGenerator.Fill(b);
        return "RDM-" + Convert.ToHexStringLower(b).ToUpperInvariant();
    }
}
