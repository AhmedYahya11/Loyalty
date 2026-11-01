using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Wallets.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Wallets.Application;

/// <summary>
/// The only component permitted to write balanced ledger postings and update wallet balance
/// projections, in the same transaction (MVP spec §13). It does NOT call SaveChanges — the
/// caller (ingestion / worker handler) owns the atomic commit.
/// </summary>
public sealed class LedgerService(IRepository<Loyalty.Core.Modules.Wallets.Domain.PointsLedgerAccount> ledgerAccountsRepo, IRepository<Loyalty.Core.Modules.Wallets.Domain.PointsLedgerTransaction> ledgerTransactionsRepo, IClock clock)
{
    /// <summary>Ensure the four application-local clearing accounts exist for a points unit.</summary>
    public async Task EnsureClearingAccountsAsync(Guid applicationId, string unit, CancellationToken ct)
    {
        var types = new[]
        {
            LedgerAccountType.IssuanceClearing, LedgerAccountType.RedemptionClearing,
            LedgerAccountType.ExpirationClearing, LedgerAccountType.AdjustmentClearing,
        };
        var existing = await ledgerAccountsRepo.Query()
            .Where(a => a.ApplicationId == applicationId && a.WalletId == null && a.PointsUnitCode == unit)
            .Select(a => a.AccountType)
            .ToListAsync(ct);

        foreach (var type in types.Except(existing))
        {
            var code = PointsLedgerAccount.ClearingCode(type, unit);
            ledgerAccountsRepo.Add(PointsLedgerAccount.Clearing(Guid.NewGuid(), applicationId, type, code, unit, clock.UtcNow));
        }
    }

    /// <summary>Create the three credit-normal member bucket accounts for a wallet.</summary>
    public void CreateMemberAccounts(Guid applicationId, Guid walletId, string unit)
    {
        foreach (var bucket in new[] { BalanceBucket.Active, BalanceBucket.Pending, BalanceBucket.Reserved })
            ledgerAccountsRepo.Add(PointsLedgerAccount.MemberBucket(Guid.NewGuid(), applicationId, walletId, bucket, unit, clock.UtcNow));
    }

    private async Task<Guid> ClearingAccountIdAsync(Guid applicationId, LedgerAccountType type, string unit, CancellationToken ct)
    {
        var code = PointsLedgerAccount.ClearingCode(type, unit);
        return await ledgerAccountsRepo.Query()
            .Where(a => a.ApplicationId == applicationId && a.AccountCode == code)
            .Select(a => a.Id)
            .FirstAsync(ct);
    }

    private async Task<Guid> MemberBucketAccountIdAsync(Guid applicationId, Guid walletId, BalanceBucket bucket, CancellationToken ct)
    {
        return await ledgerAccountsRepo.Query()
            .Where(a => a.ApplicationId == applicationId && a.WalletId == walletId && a.BalanceBucket == bucket)
            .Select(a => a.Id)
            .FirstAsync(ct);
    }

    /// <summary>
    /// Post an Earn: debit issuance clearing, credit the member active account; active
    /// balance +points. Tracks the header, postings, and wallet delta on the DbContext.
    /// </summary>
    public async Task<PointsLedgerTransaction> PostEarnAsync(
        Wallet wallet, long points, Guid sourceEventId, string sourceSystem, string sourceTransactionId,
        string businessEventKey, string correlationId, Guid? campaignId, string effectId,
        DateTimeOffset occurredAt, CancellationToken ct)
    {
        if (points <= 0) throw new InvalidOperationException("Earn points must be positive.");
        var unit = wallet.PointsUnitCode;
        var issuance = await ClearingAccountIdAsync(wallet.ApplicationId, LedgerAccountType.IssuanceClearing, unit, ct);
        var active = await MemberBucketAccountIdAsync(wallet.ApplicationId, wallet.Id, BalanceBucket.Active, ct);

        var legs = new List<LedgerLeg>
        {
            new(issuance, Debit: points, Credit: 0),
            new(active, Debit: 0, Credit: points),
        };

        var tx = PointsLedgerTransaction.Create(
            Guid.NewGuid(), wallet.ApplicationId, wallet.Id, TransactionType.Earn, unit,
            sourceEventId, sourceSystem, sourceTransactionId, businessEventKey, correlationId,
            campaignId, ruleId: null, effectId, reversalOf: null, occurredAt, clock.UtcNow, legs);

        ledgerTransactionsRepo.Add(tx);
        wallet.ApplyDelta(active: points, pending: 0, reserved: 0);
        return tx;
    }

    /// <summary>
    /// Post a signed Adjust: positive debits adjustment clearing / credits member active
    /// (+balance); negative debits member active / credits adjustment clearing (−balance).
    /// </summary>
    public async Task<PointsLedgerTransaction> PostAdjustAsync(
        Wallet wallet, long signedPoints, Guid sourceEventId, string sourceSystem, string sourceTransactionId,
        string businessEventKey, string correlationId, string effectId, DateTimeOffset occurredAt, CancellationToken ct)
    {
        if (signedPoints == 0) throw new InvalidOperationException("Adjustment must be non-zero.");
        var unit = wallet.PointsUnitCode;
        var adjustment = await ClearingAccountIdAsync(wallet.ApplicationId, LedgerAccountType.AdjustmentClearing, unit, ct);
        var active = await MemberBucketAccountIdAsync(wallet.ApplicationId, wallet.Id, BalanceBucket.Active, ct);
        var amount = Math.Abs(signedPoints);

        var (legs, activeDelta) = signedPoints > 0
            ? (new List<LedgerLeg> { new(adjustment, amount, 0), new(active, 0, amount) }, amount)
            : (new List<LedgerLeg> { new(active, amount, 0), new(adjustment, 0, amount) }, -amount);

        var tx = PointsLedgerTransaction.Create(
            Guid.NewGuid(), wallet.ApplicationId, wallet.Id, TransactionType.Adjust, unit,
            sourceEventId, sourceSystem, sourceTransactionId, businessEventKey, correlationId,
            campaignId: null, ruleId: null, effectId, reversalOf: null, occurredAt, clock.UtcNow, legs);

        ledgerTransactionsRepo.Add(tx);
        wallet.ApplyDelta(active: activeDelta, pending: 0, reserved: 0);
        return tx;
    }

    /// <summary>Reserve: debit member active, credit member reserved (active −n, reserved +n) — §13.</summary>
    public Task<PointsLedgerTransaction> PostReserveAsync(
        Wallet wallet, long points, Guid sourceEventId, string sourceSystem, string sourceTransactionId,
        string businessEventKey, string correlationId, string effectId, DateTimeOffset occurredAt, CancellationToken ct) =>
        PostBucketMoveAsync(wallet, points, TransactionType.Reserve, BalanceBucket.Active, BalanceBucket.Reserved,
            activeDelta: -points, reservedDelta: points, sourceEventId, sourceSystem, sourceTransactionId, businessEventKey, correlationId, effectId, occurredAt, ct);

    /// <summary>Release: debit member reserved, credit member active (reserved −n, active +n) — §13.</summary>
    public Task<PointsLedgerTransaction> PostReleaseAsync(
        Wallet wallet, long points, Guid sourceEventId, string sourceSystem, string sourceTransactionId,
        string businessEventKey, string correlationId, string effectId, DateTimeOffset occurredAt, CancellationToken ct) =>
        PostBucketMoveAsync(wallet, points, TransactionType.Release, BalanceBucket.Reserved, BalanceBucket.Active,
            activeDelta: points, reservedDelta: -points, sourceEventId, sourceSystem, sourceTransactionId, businessEventKey, correlationId, effectId, occurredAt, ct);

    private async Task<PointsLedgerTransaction> PostBucketMoveAsync(
        Wallet wallet, long points, TransactionType type, BalanceBucket debitBucket, BalanceBucket creditBucket,
        long activeDelta, long reservedDelta, Guid sourceEventId, string sourceSystem, string sourceTransactionId,
        string businessEventKey, string correlationId, string effectId, DateTimeOffset occurredAt, CancellationToken ct)
    {
        if (points <= 0) throw new InvalidOperationException("Amount must be positive.");
        var debit = await MemberBucketAccountIdAsync(wallet.ApplicationId, wallet.Id, debitBucket, ct);
        var credit = await MemberBucketAccountIdAsync(wallet.ApplicationId, wallet.Id, creditBucket, ct);
        var legs = new List<LedgerLeg> { new(debit, points, 0), new(credit, 0, points) };
        var tx = PointsLedgerTransaction.Create(
            Guid.NewGuid(), wallet.ApplicationId, wallet.Id, type, wallet.PointsUnitCode,
            sourceEventId, sourceSystem, sourceTransactionId, businessEventKey, correlationId,
            campaignId: null, ruleId: null, effectId, reversalOf: null, occurredAt, clock.UtcNow, legs);
        ledgerTransactionsRepo.Add(tx);
        wallet.ApplyDelta(active: activeDelta, pending: 0, reserved: reservedDelta);
        return tx;
    }

    /// <summary>Redeem reserved points: debit member reserved, credit redemption clearing (reserved −n) — §13.</summary>
    public async Task<PointsLedgerTransaction> PostRedeemAsync(
        Wallet wallet, long points, Guid sourceEventId, string sourceSystem, string sourceTransactionId,
        string businessEventKey, string correlationId, string effectId, DateTimeOffset occurredAt, CancellationToken ct)
    {
        if (points <= 0) throw new InvalidOperationException("Redeem points must be positive.");
        var reserved = await MemberBucketAccountIdAsync(wallet.ApplicationId, wallet.Id, BalanceBucket.Reserved, ct);
        var redemption = await ClearingAccountIdAsync(wallet.ApplicationId, LedgerAccountType.RedemptionClearing, wallet.PointsUnitCode, ct);
        var legs = new List<LedgerLeg> { new(reserved, points, 0), new(redemption, 0, points) };
        var tx = PointsLedgerTransaction.Create(
            Guid.NewGuid(), wallet.ApplicationId, wallet.Id, TransactionType.Redeem, wallet.PointsUnitCode,
            sourceEventId, sourceSystem, sourceTransactionId, businessEventKey, correlationId,
            campaignId: null, ruleId: null, effectId, reversalOf: null, occurredAt, clock.UtcNow, legs);
        ledgerTransactionsRepo.Add(tx);
        wallet.ApplyDelta(active: 0, pending: 0, reserved: -points);
        return tx;
    }

    /// <summary>Expire points: debit member active, credit expiration clearing (active −n) — §13, P13.</summary>
    public async Task<PointsLedgerTransaction> PostExpireAsync(
        Wallet wallet, long points, Guid sourceEventId, string sourceSystem, string sourceTransactionId,
        string businessEventKey, string correlationId, string effectId, DateTimeOffset occurredAt, CancellationToken ct)
    {
        if (points <= 0) throw new InvalidOperationException("Expire points must be positive.");
        var active = await MemberBucketAccountIdAsync(wallet.ApplicationId, wallet.Id, BalanceBucket.Active, ct);
        var expiration = await ClearingAccountIdAsync(wallet.ApplicationId, LedgerAccountType.ExpirationClearing, wallet.PointsUnitCode, ct);
        var legs = new List<LedgerLeg> { new(active, points, 0), new(expiration, 0, points) };
        var tx = PointsLedgerTransaction.Create(
            Guid.NewGuid(), wallet.ApplicationId, wallet.Id, TransactionType.Expire, wallet.PointsUnitCode,
            sourceEventId, sourceSystem, sourceTransactionId, businessEventKey, correlationId,
            campaignId: null, ruleId: null, effectId, reversalOf: null, occurredAt, clock.UtcNow, legs);
        ledgerTransactionsRepo.Add(tx);
        wallet.ApplyDelta(active: -points, pending: 0, reserved: 0);
        return tx;
    }

    /// <summary>
    /// Reverse an Earn: debit member active / credit issuance clearing by the original credit
    /// amount, reducing active balance. References the original via ReversalOfTransactionId (§13).
    /// </summary>
    public async Task<PointsLedgerTransaction> PostReverseAsync(
        Wallet wallet, PointsLedgerTransaction original, Guid sourceEventId, string sourceSystem, string sourceTransactionId,
        string businessEventKey, string correlationId, string effectId, DateTimeOffset occurredAt, CancellationToken ct)
    {
        var unit = wallet.PointsUnitCode;
        var issuance = await ClearingAccountIdAsync(wallet.ApplicationId, LedgerAccountType.IssuanceClearing, unit, ct);
        var active = await MemberBucketAccountIdAsync(wallet.ApplicationId, wallet.Id, BalanceBucket.Active, ct);
        var amount = original.TotalCredit;

        var legs = new List<LedgerLeg> { new(active, amount, 0), new(issuance, 0, amount) };
        var tx = PointsLedgerTransaction.Create(
            Guid.NewGuid(), wallet.ApplicationId, wallet.Id, TransactionType.Reverse, unit,
            sourceEventId, sourceSystem, sourceTransactionId, businessEventKey, correlationId,
            campaignId: original.CampaignId, ruleId: null, effectId, reversalOf: original.Id, occurredAt, clock.UtcNow, legs);

        ledgerTransactionsRepo.Add(tx);
        wallet.ApplyDelta(active: -amount, pending: 0, reserved: 0);
        return tx;
    }
}
