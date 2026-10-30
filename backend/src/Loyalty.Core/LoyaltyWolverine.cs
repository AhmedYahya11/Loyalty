using Loyalty.Core.Modules.Integration.Contracts;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;

namespace Loyalty.Core;

/// <summary>
/// Shared Wolverine configuration. Phase 1 uses a durable local queue backed by PostgreSQL
/// (Kafka and the cross-process Postgres transport are deferred — MVP spec §3, §20). Messages
/// are persisted transactionally with the ingestion request and processed asynchronously.
/// </summary>
public static class LoyaltyWolverine
{
    public const string ProcessQueue = "process-loyalty-event";

    public static void Configure(WolverineOptions opts, string connectionString)
    {
        // handlers live in Loyalty.Core, not the entry assembly
        opts.Discovery.IncludeAssembly(typeof(LoyaltyWolverine).Assembly);

        opts.PersistMessagesWithPostgresql(connectionString);
        opts.UseEntityFrameworkCoreTransactions();

        // make local queues durable so accepted work survives a restart
        opts.Policies.UseDurableLocalQueues();

        // retry transient failures (e.g. reading the ingestion row just after commit) with backoff
        opts.OnException<InvalidOperationException>()
            .RetryWithCooldown(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        opts.PublishMessage<ProcessLoyaltyEvent>().ToLocalQueue(ProcessQueue);
    }
}
