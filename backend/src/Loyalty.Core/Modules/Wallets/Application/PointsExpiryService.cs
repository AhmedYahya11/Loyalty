using System.Text.Json;
using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Notifications.Domain;
using Loyalty.Core.Modules.Wallets.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Wallets.Application;

/// <summary>
/// Expires points that were earned longer ago than the application's <c>PointsExpiryDays</c> window
/// and were never spent (P13). FIFO: the oldest earned points are consumed first by redemptions,
/// reversals and prior expirations, so whatever remains of the lots older than the cutoff is what
/// expires now. Reserved (mid-redemption) and manually-granted points are protected because the
/// expiry amount is capped by the current Active balance. Runs in system scope; idempotent — a
/// re-run finds the just-posted Expire in "consumed" and computes zero.
/// </summary>
public sealed class PointsExpiryService(LoyaltyDbContext db, ApplicationContext scope, LedgerService ledger, IClock clock)
{
    public Task<long> RunAsync(CancellationToken ct) => RunForAsync(clock.UtcNow, ct);

    public async Task<long> RunForAsync(DateTimeOffset asOf, CancellationToken ct)
    {
        scope.EnterSystemScope();
        try
        {
            var policies = await db.LoyaltySettings.Where(s => s.PointsExpiryDays > 0)
                .Select(s => new { s.ApplicationId, s.PointsExpiryDays }).ToListAsync(ct);

            long expiredTotal = 0;
            foreach (var p in policies)
            {
                var cutoff = asOf.AddDays(-p.PointsExpiryDays);
                var wallets = await db.Wallets.Where(w => w.ApplicationId == p.ApplicationId).ToListAsync(ct);
                foreach (var wallet in wallets)
                {
                    if (wallet.ActiveBalance <= 0) continue;

                    var txns = await db.LedgerTransactions.Where(t => t.WalletId == wallet.Id).ToListAsync(ct);
                    var oldEarned = txns.Where(t => t.TransactionType == TransactionType.Earn && t.OccurredAt <= cutoff).Sum(t => t.TotalCredit);
                    if (oldEarned <= 0) continue;

                    // consumption that removed earned points, FIFO (oldest first): redemptions, reversals, prior expirations
                    var consumed =
                        txns.Where(t => t.TransactionType == TransactionType.Redeem).Sum(t => t.TotalDebit)
                        + txns.Where(t => t.TransactionType == TransactionType.Reverse).Sum(t => t.TotalDebit)
                        + txns.Where(t => t.TransactionType == TransactionType.Expire).Sum(t => t.TotalDebit);

                    var remainingOld = oldEarned - consumed;
                    var expire = Math.Min(remainingOld, wallet.ActiveBalance);
                    if (expire <= 0) continue;

                    var now = clock.UtcNow;
                    var sourceEventId = Guid.NewGuid();
                    var effectId = $"expiry:{cutoff.UtcDateTime:yyyy-MM-dd}";
                    await ledger.PostExpireAsync(wallet, expire, sourceEventId, "system",
                        $"expiry-{cutoff.UtcDateTime:yyyyMMdd}", $"expiry|{wallet.Id}|{cutoff.UtcDateTime:yyyyMMdd}", "expiry", effectId, now, ct);

                    var profile = await db.ValueProfiles.FirstOrDefaultAsync(vp => vp.MemberId == wallet.MemberId, ct);
                    profile?.AddPointsExpired(expire);
                    db.EventLog.Add(EventLogEntry.Create(p.ApplicationId, ResultEventTypes.PointsExpired, wallet.MemberId, null,
                        JsonSerializer.Serialize(new { memberId = wallet.MemberId, expiredPoints = expire, pointsUnitCode = wallet.PointsUnitCode }, JsonOptionsProvider.Default), now, now));
                    expiredTotal += expire;
                }
                await db.SaveChangesAsync(ct);
            }
            return expiredTotal;
        }
        finally { scope.LeaveSystemScope(); }
    }
}
