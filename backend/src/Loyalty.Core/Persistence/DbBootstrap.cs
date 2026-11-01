using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Persistence;

/// <summary>
/// Applies EF Core migrations and installs the deferred balancing trigger that enforces
/// double-entry integrity in PostgreSQL (defense in depth beyond the domain — MVP spec §13).
/// Migration ordering is enforced, not assumed (A2): the API holds a PostgreSQL advisory lock while
/// migrating (so concurrent API replicas serialize instead of racing), and the Worker calls
/// <see cref="WaitForSchemaAsync"/> to block until the schema is up to date before it consumes.
/// </summary>
public static class DbBootstrap
{
    // stable, arbitrary key so every host contends on the same advisory lock
    private const long MigrationLockKey = 912_730_017L;

    /// <summary>Apply migrations + the trigger under a cross-process advisory lock. Call from the API
    /// (the single migration authority). Safe under concurrent replicas — the lock serializes them and
    /// the already-applied migrations become a no-op for the losers.</summary>
    public static async Task InitializeAsync(LoyaltyDbContext db, CancellationToken ct = default)
    {
        var conn = db.Database.GetDbConnection();
        var openedHere = conn.State != System.Data.ConnectionState.Open;
        if (openedHere) await conn.OpenAsync(ct);
        try
        {
            await ExecScalarAsync(conn, "SELECT pg_advisory_lock(@key)", ct);   // blocks until acquired
            try
            {
                await db.Database.MigrateAsync(ct);
                await db.Database.ExecuteSqlRawAsync(BalancingTriggerSql, ct);
            }
            finally { await ExecScalarAsync(conn, "SELECT pg_advisory_unlock(@key)", ct); }
        }
        finally { if (openedHere) await conn.CloseAsync(); }
    }

    /// <summary>Block until all migrations are applied (no pending). Call from hosts that must NOT
    /// migrate themselves (the Worker) so they never run against a missing/stale schema (A2).</summary>
    public static async Task WaitForSchemaAsync(LoyaltyDbContext db, int maxAttempts = 60, int delaySeconds = 2, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (!(await db.Database.GetPendingMigrationsAsync(ct)).Any()) return;
            }
            catch { /* DB not reachable yet, or the migrations-history table doesn't exist — keep waiting */ }

            if (attempt >= maxAttempts)
                throw new InvalidOperationException("Timed out waiting for database migrations to be applied (is the API running?).");
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
        }
    }

    private static async Task ExecScalarAsync(System.Data.Common.DbConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var p = cmd.CreateParameter();
        p.ParameterName = "key";
        p.Value = MigrationLockKey;
        cmd.Parameters.Add(p);
        await cmd.ExecuteNonQueryAsync(ct);
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
