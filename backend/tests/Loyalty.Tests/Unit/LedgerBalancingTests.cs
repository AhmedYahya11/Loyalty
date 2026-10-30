using Loyalty.Core.Modules.Wallets.Domain;
using Shouldly;

namespace Loyalty.Tests.Unit;

public class LedgerBalancingTests
{
    private static LedgerLeg Debit(long n) => new(Guid.NewGuid(), n, 0);
    private static LedgerLeg Credit(long n) => new(Guid.NewGuid(), 0, n);

    [Fact]
    public void Balanced_two_legs_creates_transaction()
    {
        var tx = PointsLedgerTransaction.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TransactionType.Earn, "POINT",
            Guid.NewGuid(), "src", "txn", "key", "corr", null, null, "effect", null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            [Debit(10), Credit(10)]);

        tx.TotalDebit.ShouldBe(10);
        tx.TotalCredit.ShouldBe(10);
        tx.Postings.Count.ShouldBe(2);
    }

    [Fact]
    public void Unbalanced_legs_throw()
    {
        Should.Throw<InvalidOperationException>(() => PointsLedgerTransaction.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TransactionType.Earn, "POINT",
            Guid.NewGuid(), "src", "txn", "key", "corr", null, null, "effect", null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            [Debit(10), Credit(7)]));
    }

    [Fact]
    public void Single_posting_throws()
    {
        Should.Throw<InvalidOperationException>(() => PointsLedgerTransaction.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TransactionType.Earn, "POINT",
            Guid.NewGuid(), "src", "txn", "key", "corr", null, null, "effect", null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            [Debit(10)]));
    }

    [Fact]
    public void Posting_cannot_be_two_sided()
    {
        Should.Throw<InvalidOperationException>(() =>
            new PointsLedgerPosting(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 5, 5, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Posting_must_be_non_negative()
    {
        Should.Throw<InvalidOperationException>(() =>
            new PointsLedgerPosting(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, -1, 0, DateTimeOffset.UtcNow));
    }
}
