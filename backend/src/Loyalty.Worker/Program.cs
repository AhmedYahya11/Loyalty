using JasperFx.Resources;
using Loyalty.Core;
using Serilog;
using Wolverine;

// Phase 1 note: the API host self-processes events via a durable local queue. This Worker
// host runs the same Wolverine configuration and handlers; it becomes the dedicated processor
// once the cross-process PostgreSQL transport is enabled (P5 — see docs/DEVIATIONS.md).

var conn = Environment.GetEnvironmentVariable("LOYALTY_DB")
    ?? "Host=localhost;Port=5432;Database=loyalty;Username=loyalty;Password=loyalty";

var host = Host.CreateDefaultBuilder(args)
    .UseSerilog((_, cfg) => cfg.WriteTo.Console())
    .UseWolverine(o => LoyaltyWolverine.Configure(o, conn))
    .ConfigureServices(services =>
    {
        services.AddLoyaltyCore(conn);
        services.AddResourceSetupOnStartup();
    })
    .Build();

await host.RunAsync();
