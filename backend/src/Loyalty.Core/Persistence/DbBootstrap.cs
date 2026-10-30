using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Persistence;

/// <summary>
/// Creates the application schema and installs the deferred balancing trigger that enforces
/// double-entry integrity in PostgreSQL (defense in depth beyond the domain — MVP spec §13).
/// Phase 1 uses EnsureCreated; production will switch to EF migrations (see docs/DEVIATIONS.md).
/// </summary>
public static class DbBootstrap
{
    public static async Task InitializeAsync(LoyaltyDbContext db, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);
        await db.Database.ExecuteSqlRawAsync(BalancingTriggerSql, ct);
    }

    // A constraint trigger, deferred to commit, asserting every ledger transaction balances:
    // at least two postings, sum(debit) = sum(credit) > 0, and totals match the header.
    private const string BalancingTriggerSql = """
        CREATE OR REPLACE FUNCTION wallets.assert_balanced_tx() RETURNS trigger AS $$
        DECLARE
            v_debit  bigint;
            v_credit bigint;
            v_count  int;
        BEGIN
            SELECT COALESCE(SUM("DebitAmount"),0), COALESCE(SUM("CreditAmount"),0), COUNT(*)
              INTO v_debit, v_credit, v_count
              FROM wallets.points_ledger_postings
             WHERE "LedgerTransactionId" = NEW."Id";

            IF v_count < 2 THEN
                RAISE EXCEPTION 'Ledger transaction % has % posting(s); at least two are required', NEW."Id", v_count;
            END IF;
            IF v_debit <> v_credit THEN
                RAISE EXCEPTION 'Ledger transaction % is unbalanced: debit % <> credit %', NEW."Id", v_debit, v_credit;
            END IF;
            IF v_debit <= 0 THEN
                RAISE EXCEPTION 'Ledger transaction % moves a non-positive amount', NEW."Id";
            END IF;
            IF NEW."TotalDebit" <> v_debit OR NEW."TotalCredit" <> v_credit THEN
                RAISE EXCEPTION 'Ledger transaction % header totals do not match postings', NEW."Id";
            END IF;
            RETURN NEW;
        END;
        $$ LANGUAGE plpgsql;

        DROP TRIGGER IF EXISTS trg_assert_balanced ON wallets.points_ledger_transactions;
        CREATE CONSTRAINT TRIGGER trg_assert_balanced
            AFTER INSERT ON wallets.points_ledger_transactions
            DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW EXECUTE FUNCTION wallets.assert_balanced_tx();
        """;
}
