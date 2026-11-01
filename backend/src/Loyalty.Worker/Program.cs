using JasperFx.Resources;
using Loyalty.Core;
using Loyalty.Core.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Wolverine;

// The dedicated processor. It runs the same Wolverine configuration + handlers as the API but is NOT a
// migration authority: the API applies migrations (under an advisory lock). To enforce ordering (A2)
// the worker waits until the schema is up to date before it starts consuming, instead of assuming it.

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

// gate startup on the schema being ready (the API migrates; the worker only waits)
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LoyaltyDbContext>();
    Console.WriteLine("Worker waiting for database migrations to be applied…");
    await DbBootstrap.WaitForSchemaAsync(db, maxAttempts: 60, delaySeconds: 2);
    Console.WriteLine("Database schema is ready; starting the worker.");
}

await host.RunAsync();
