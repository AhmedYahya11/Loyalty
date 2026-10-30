using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Wallets.Domain;

public enum WalletType { Default = 0 }

/// <summary>
/// A member's points wallet. The three balances are real stored columns (fast projections,
/// not the source of truth) updated only by the ledger posting service in the same
/// transaction as balanced postings. Version is an optimistic-concurrency token (§13).
/// </summary>
public sealed class Wallet : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid MemberId { get; private set; }
    public WalletType WalletType { get; private set; }
    public string PointsUnitCode { get; private set; } = default!;
    public long ActiveBalance { get; private set; }
    public long PendingBalance { get; private set; }
    public long ReservedBalance { get; private set; }
    public int Version { get; private set; }

    private Wallet() { }

    public static Wallet Create(Guid id, Guid applicationId, Guid memberId, string pointsUnitCode) => new()
    {
        Id = id,
        ApplicationId = applicationId,
        MemberId = memberId,
        WalletType = WalletType.Default,
        PointsUnitCode = pointsUnitCode,
        ActiveBalance = 0,
        PendingBalance = 0,
        ReservedBalance = 0,
        Version = 1,
    };

    /// <summary>
    /// Apply a bucket delta. Called only by the ledger posting service, inside the same
    /// transaction that writes the balanced postings.
    /// </summary>
    public void ApplyDelta(long active, long pending, long reserved)
    {
        ActiveBalance += active;
        PendingBalance += pending;
        ReservedBalance += reserved;
        Version += 1;
    }
}
