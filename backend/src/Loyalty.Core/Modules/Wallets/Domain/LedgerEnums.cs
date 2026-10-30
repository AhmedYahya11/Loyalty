namespace Loyalty.Core.Modules.Wallets.Domain;

/// <summary>Accounting account types required by the ledger model (MVP spec §13).</summary>
public enum LedgerAccountType
{
    MemberWallet = 0,
    IssuanceClearing = 1,
    RedemptionClearing = 2,
    ExpirationClearing = 3,
    AdjustmentClearing = 4,
}

/// <summary>Which wallet balance bucket a member ledger account projects into.</summary>
public enum BalanceBucket { Active = 0, Pending = 1, Reserved = 2 }

public enum NormalBalance { Debit = 0, Credit = 1 }

/// <summary>Ledger transaction types. MVP implements Earn, Reverse, and Adjust (§13).</summary>
public enum TransactionType
{
    Earn = 0,
    Reverse = 1,
    Adjust = 2,
    Reserve = 3,
    Release = 4,
    Redeem = 5,
    Expire = 6,
}
