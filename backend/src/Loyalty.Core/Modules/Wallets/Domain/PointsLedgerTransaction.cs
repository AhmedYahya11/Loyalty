using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Wallets.Domain;

/// <summary>One leg of a transaction before the posting row is built.</summary>
public readonly record struct LedgerLeg(Guid LedgerAccountId, long Debit, long Credit);

/// <summary>
/// Immutable double-entry transaction header. Every completed transaction has at least two
/// postings, all sharing the header's application and points unit, with
/// sum(debit) = sum(credit) > 0. Balancing is enforced here in the domain and again in
/// PostgreSQL (MVP spec §13).
/// </summary>
public sealed class PointsLedgerTransaction : IApplicationScoped
{
    private readonly List<PointsLedgerPosting> _postings = [];

    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid WalletId { get; private set; }
    public TransactionType TransactionType { get; private set; }
    public string PointsUnitCode { get; private set; } = default!;
    public long TotalDebit { get; private set; }
    public long TotalCredit { get; private set; }
    public Guid SourceEventId { get; private set; }
    public string SourceSystem { get; private set; } = default!;
    public string SourceTransactionId { get; private set; } = default!;
    public string BusinessEventKey { get; private set; } = default!;
    public string CorrelationId { get; private set; } = default!;
    public Guid? CampaignId { get; private set; }
    public Guid? RuleId { get; private set; }
    public string EffectId { get; private set; } = default!;
    public Guid? ReversalOfTransactionId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<PointsLedgerPosting> Postings => _postings;

    private PointsLedgerTransaction() { }

    public static PointsLedgerTransaction Create(
        Guid id, Guid applicationId, Guid walletId, TransactionType type, string pointsUnitCode,
        Guid sourceEventId, string sourceSystem, string sourceTransactionId, string businessEventKey,
        string correlationId, Guid? campaignId, Guid? ruleId, string effectId,
        Guid? reversalOf, DateTimeOffset occurredAt, DateTimeOffset now,
        IReadOnlyList<LedgerLeg> legs)
    {
        if (legs.Count < 2)
            throw new InvalidOperationException("A ledger transaction requires at least two postings.");

        var tx = new PointsLedgerTransaction
        {
            Id = id,
            ApplicationId = applicationId,
            WalletId = walletId,
            TransactionType = type,
            PointsUnitCode = pointsUnitCode,
            SourceEventId = sourceEventId,
            SourceSystem = sourceSystem,
            SourceTransactionId = sourceTransactionId,
            BusinessEventKey = businessEventKey,
            CorrelationId = correlationId,
            CampaignId = campaignId,
            RuleId = ruleId,
            EffectId = effectId,
            ReversalOfTransactionId = reversalOf,
            OccurredAt = occurredAt,
            CreatedAt = now,
        };

        var seq = 1;
        foreach (var leg in legs)
        {
            var posting = new PointsLedgerPosting(
                Guid.NewGuid(), applicationId, leg.LedgerAccountId, seq++, leg.Debit, leg.Credit, now);
            posting.AttachTo(id);
            tx._postings.Add(posting);
        }

        tx.TotalDebit = tx._postings.Sum(p => p.DebitAmount);
        tx.TotalCredit = tx._postings.Sum(p => p.CreditAmount);

        if (tx.TotalDebit != tx.TotalCredit)
            throw new InvalidOperationException(
                $"Unbalanced transaction: debit {tx.TotalDebit} != credit {tx.TotalCredit}.");
        if (tx.TotalDebit <= 0)
            throw new InvalidOperationException("A ledger transaction must move a positive amount.");

        return tx;
    }
}
