using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Wallets.Domain;

/// <summary>
/// An accounting account (unrelated to the API-credential Account). Member wallet buckets
/// are credit-normal accounts; internal clearing accounts have no WalletId (MVP spec §13).
/// </summary>
public sealed class PointsLedgerAccount : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public string AccountCode { get; private set; } = default!;
    public LedgerAccountType AccountType { get; private set; }
    public Guid? WalletId { get; private set; }
    public BalanceBucket? BalanceBucket { get; private set; }
    public string PointsUnitCode { get; private set; } = default!;
    public NormalBalance NormalBalance { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private PointsLedgerAccount() { }

    public static PointsLedgerAccount MemberBucket(
        Guid id, Guid applicationId, Guid walletId, BalanceBucket bucket,
        string pointsUnitCode, DateTimeOffset now) => new()
    {
        Id = id,
        ApplicationId = applicationId,
        AccountCode = $"WALLET-{walletId:N}-{bucket.ToString().ToUpperInvariant()}",
        AccountType = LedgerAccountType.MemberWallet,
        WalletId = walletId,
        BalanceBucket = bucket,
        PointsUnitCode = pointsUnitCode,
        NormalBalance = NormalBalance.Credit,
        IsActive = true,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public static PointsLedgerAccount Clearing(
        Guid id, Guid applicationId, LedgerAccountType type, string accountCode,
        string pointsUnitCode, DateTimeOffset now) => new()
    {
        Id = id,
        ApplicationId = applicationId,
        AccountCode = accountCode,
        AccountType = type,
        WalletId = null,
        BalanceBucket = null,
        PointsUnitCode = pointsUnitCode,
        NormalBalance = NormalBalance.Debit,
        IsActive = true,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public static string ClearingCode(LedgerAccountType type, string unit) => type switch
    {
        LedgerAccountType.IssuanceClearing => $"POINTS_ISSUANCE_{unit}",
        LedgerAccountType.RedemptionClearing => $"POINTS_REDEMPTION_{unit}",
        LedgerAccountType.ExpirationClearing => $"POINTS_EXPIRATION_{unit}",
        LedgerAccountType.AdjustmentClearing => $"POINTS_ADJUSTMENT_{unit}",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a clearing account type."),
    };
}
