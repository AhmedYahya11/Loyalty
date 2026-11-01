using Loyalty.Core.Modules.Integration.Contracts;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

namespace Loyalty.Core;

/// <summary>
/// Shared Wolverine configuration. The message STORE is always the durable PostgreSQL EF-core
/// outbox, so publishing is transactional with the ingestion write. The TRANSPORT is selectable:
/// RabbitMQ when a broker is configured (LOYALTY_BROKER=rabbitmq + RABBITMQ_URL), otherwise the
/// built-in durable PostgreSQL queue. Either way the API publishes and the dedicated Loyalty.Worker
/// (or the API host) consumes as competing consumers. Kafka remains deferred (MVP spec §3, §20).
/// </summary>
public static class LoyaltyWolverine
{
    public const string ProcessQueue = "process-loyalty-event";

    /// <summary>True when RabbitMQ is selected via environment (else the durable Postgres transport).</summary>
    public static bool UseRabbit =>
        string.Equals(Environment.GetEnvironmentVariable("LOYALTY_BROKER"), "rabbitmq", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RABBITMQ_URL"));

    public static void Configure(WolverineOptions opts, string connectionString)
    {
        // handlers live in Loyalty.Core, not the entry assembly
        opts.Discovery.IncludeAssembly(typeof(LoyaltyWolverine).Assembly);

        // retry transient failures (e.g. reading the ingestion row just after commit) with backoff
        opts.OnException<InvalidOperationException>()
            .RetryWithCooldown(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        // concurrent events for a brand-new member can race on provisioning (unique-constraint
        // violation) — the loser retries, finds the now-existing member, and completes. Safe
        // because effect/ledger uniqueness + the terminal-status check make reprocessing a no-op.
        opts.OnException<DbUpdateException>()
            .RetryWithCooldown(TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(750), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));

        if (UseRabbit)
        {
            // durable Postgres message store (transactional outbox) + RabbitMQ transport
            opts.PersistMessagesWithPostgresql(connectionString);
            opts.UseEntityFrameworkCoreTransactions();

            opts.UseRabbitMq(new Uri(Environment.GetEnvironmentVariable("RABBITMQ_URL")!))
                .AutoProvision()
                .DeclareQueue(ProcessQueue, q => q.IsDurable = true);

            // outbox-published to the broker; the worker/API consume with a durable inbox
            opts.PublishMessage<ProcessLoyaltyEvent>().ToRabbitQueue(ProcessQueue);
            opts.ListenToRabbitQueue(ProcessQueue).UseDurableInbox();
            return;
        }

        // default: durable PostgreSQL message store + transport (queues live in the database)
        opts.UsePostgresqlPersistenceAndTransport(connectionString);
        opts.UseEntityFrameworkCoreTransactions();

        // publish to and listen on the durable Postgres queue (every host both publishes and
        // can consume; run Loyalty.Worker for a dedicated processor pool)
        opts.PublishMessage<ProcessLoyaltyEvent>().ToPostgresqlQueue(ProcessQueue);
        opts.ListenToPostgresqlQueue(ProcessQueue);
    }
}
