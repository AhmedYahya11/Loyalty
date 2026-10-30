using Loyalty.Core.Modules.Integration.Contracts;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;

namespace Loyalty.Core;

/// <summary>
/// Shared Wolverine configuration. Uses the durable PostgreSQL persistence + transport so the
/// API publishes to a durable queue and either the API host or the dedicated Loyalty.Worker
/// consumes it (cross-process competing consumers). Kafka remains deferred (MVP spec §3, §20).
/// </summary>
public static class LoyaltyWolverine
{
    public const string ProcessQueue = "process-loyalty-event";

    public static void Configure(WolverineOptions opts, string connectionString)
    {
        // handlers live in Loyalty.Core, not the entry assembly
        opts.Discovery.IncludeAssembly(typeof(LoyaltyWolverine).Assembly);

        // durable PostgreSQL message store + transport (queues live in the database)
        opts.UsePostgresqlPersistenceAndTransport(connectionString);
        opts.UseEntityFrameworkCoreTransactions();

        // retry transient failures (e.g. reading the ingestion row just after commit) with backoff
        opts.OnException<InvalidOperationException>()
            .RetryWithCooldown(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        // publish to and listen on the durable Postgres queue (every host both publishes and
        // can consume; run Loyalty.Worker for a dedicated processor pool)
        opts.PublishMessage<ProcessLoyaltyEvent>().ToPostgresqlQueue(ProcessQueue);
        opts.ListenToPostgresqlQueue(ProcessQueue);
    }
}
