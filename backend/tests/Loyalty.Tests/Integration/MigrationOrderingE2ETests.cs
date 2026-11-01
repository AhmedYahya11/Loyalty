using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Loyalty.Tests.Integration;

/// <summary>
/// A2 — migration ordering is enforced, not assumed. On a fresh database the worker's wait gate blocks
/// (it must not run before the schema exists); two concurrent API "replicas" migrating at once are
/// serialized by the advisory lock and both succeed; afterwards the wait gate returns immediately.
/// Uses its own PostgreSQL container so it doesn't touch the shared fixture database.
/// </summary>
public sealed class MigrationOrderingE2ETests
{
    [Fact]
    public async Task Worker_waits_for_schema_and_concurrent_migrations_are_serialized()
    {
        await using var pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
        await pg.StartAsync();
        var cs = pg.GetConnectionString();

        static ApplicationContext Sys() { var c = new ApplicationContext(); c.EnterSystemScope(); return c; }
        LoyaltyDbContext New() => new(new DbContextOptionsBuilder<LoyaltyDbContext>().UseNpgsql(cs).Options, Sys());

        // before the API has migrated, the worker's gate must NOT pass (it waits, not assumes)
        await using (var early = New())
            await Should.ThrowAsync<InvalidOperationException>(
                () => DbBootstrap.WaitForSchemaAsync(early, maxAttempts: 1, delaySeconds: 1));

        // two API replicas migrate concurrently — the advisory lock serializes them; both complete
        async Task Init() { await using var db = New(); await DbBootstrap.InitializeAsync(db); }
        await Task.WhenAll(Init(), Init());

        // schema is ready now → the worker's gate returns without waiting, and nothing is pending
        await using var check = New();
        await DbBootstrap.WaitForSchemaAsync(check, maxAttempts: 1, delaySeconds: 1);
        (await check.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();

        await pg.DisposeAsync();
    }
}
