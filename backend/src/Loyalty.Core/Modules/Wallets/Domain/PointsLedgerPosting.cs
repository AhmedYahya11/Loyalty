using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Wallets.Domain;

/// <summary>
/// One leg of a balanced ledger transaction. Exactly one of DebitAmount / CreditAmount is
/// greater than zero; both are non-negative (MVP spec §13). Immutable.
/// </summary>
public sealed class PointsLedgerPosting : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid LedgerTransactionId { get; private set; }
    public Guid LedgerAccountId { get; private set; }
    public int Sequence { get; private set; }
    public long DebitAmount { get; private set; }
    public long CreditAmount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private PointsLedgerPosting() { }

    public PointsLedgerPosting(
        Guid id, Guid applicationId, Guid ledgerAccountId, int sequence,
        long debitAmount, long creditAmount, DateTimeOffset now)
    {
        if (debitAmount < 0 || creditAmount < 0)
            throw new InvalidOperationException("Posting amounts must be non-negative.");
        if ((debitAmount > 0) == (creditAmount > 0))
            throw new InvalidOperationException("Exactly one of debit or credit must be greater than zero.");

        Id = id;
        ApplicationId = applicationId;
        LedgerAccountId = ledgerAccountId;
        Sequence = sequence;
        DebitAmount = debitAmount;
        CreditAmount = creditAmount;
        CreatedAt = now;
    }

    internal void AttachTo(Guid transactionId) => LedgerTransactionId = transactionId;
}
