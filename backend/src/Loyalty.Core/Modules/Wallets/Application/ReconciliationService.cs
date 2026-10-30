using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Wallets.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Wallets.Application;

public sealed record BucketCheck(long Stored, long Computed, bool Match);
public sealed record ReconciliationReport(
    Guid MemberId, Guid WalletId, BucketCheck Active, BucketCheck Pending, BucketCheck Reserved,
    BucketCheck PointsEarned, bool Balanced, bool Repaired);

/// <summary>
/// Recomputes wallet balance projections from the immutable ledger postings (credits − debits on
/// each member bucket account) and reports mismatches; optionally repairs the projection without
/// ever touching the postings (MVP spec §13). Also checks profile PointsEarned against the ledger.
/// </summary>
public sealed class ReconciliationService(LoyaltyDbContext db)
{
    public async Task<ReconciliationReport?> ReconcileMemberAsync(Guid memberId, bool repair, CancellationToken ct)
    {
        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == memberId, ct);
        if (member is null) return null;
        var wallet = await db.Wallets.FirstAsync(w => w.Id == member.DefaultWalletId, ct);

        var active = await BucketBalanceAsync(wallet.Id, BalanceBucket.Active, ct);
        var pending = await BucketBalanceAsync(wallet.Id, BalanceBucket.Pending, ct);
        var reserved = await BucketBalanceAsync(wallet.Id, BalanceBucket.Reserved, ct);

        var activeChk = new BucketCheck(wallet.ActiveBalance, active, wallet.ActiveBalance == active);
        var pendingChk = new BucketCheck(wallet.PendingBalance, pending, wallet.PendingBalance == pending);
        var reservedChk = new BucketCheck(wallet.ReservedBalance, reserved, wallet.ReservedBalance == reserved);

        // profile points earned = net Earn credits to the member active account (earn − reverse)
        var profile = await db.ValueProfiles.FirstOrDefaultAsync(p => p.MemberId == memberId, ct);
        var earnedComputed = await db.LedgerTransactions
            .Where(t => t.WalletId == wallet.Id && t.TransactionType == TransactionType.Earn)
            .SumAsync(t => (long?)t.TotalCredit, ct) ?? 0;
        var earnedChk = new BucketCheck(profile?.PointsEarned ?? 0, earnedComputed, (profile?.PointsEarned ?? 0) == earnedComputed);

        var balanced = activeChk.Match && pendingChk.Match && reservedChk.Match;

        var repaired = false;
        if (repair && !balanced)
        {
            wallet.ReconcileTo(active, pending, reserved);
            await db.SaveChangesAsync(ct);
            repaired = true;
            activeChk = activeChk with { Stored = active, Match = true };
            pendingChk = pendingChk with { Stored = pending, Match = true };
            reservedChk = reservedChk with { Stored = reserved, Match = true };
        }

        return new ReconciliationReport(memberId, wallet.Id, activeChk, pendingChk, reservedChk, earnedChk, balanced, repaired);
    }

    private async Task<long> BucketBalanceAsync(Guid walletId, BalanceBucket bucket, CancellationToken ct)
    {
        var accountId = await db.LedgerAccounts
            .Where(a => a.WalletId == walletId && a.BalanceBucket == bucket)
            .Select(a => a.Id).FirstAsync(ct);
        var credits = await db.LedgerPostings.Where(p => p.LedgerAccountId == accountId).SumAsync(p => (long?)p.CreditAmount, ct) ?? 0;
        var debits = await db.LedgerPostings.Where(p => p.LedgerAccountId == accountId).SumAsync(p => (long?)p.DebitAmount, ct) ?? 0;
        return credits - debits; // credit-normal member accounts
    }
}
