using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Loyalty.Core.Modules.Cvm.Application;

/// <summary>
/// Hosted background job that runs the daily lifecycle sweep. The first run is delayed so it
/// never interferes with request-time processing or tests; thereafter it runs once per day.
/// </summary>
public sealed class LifecycleSweepJob(IServiceScopeFactory scopeFactory, ILogger<LifecycleSweepJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(InitialDelay, stoppingToken); } catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var sweep = scope.ServiceProvider.GetRequiredService<LifecycleSweepService>();
                var n = await sweep.RunAsync(stoppingToken);
                logger.LogInformation("Lifecycle sweep complete: {Transitions} transition(s)", n);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.LogError(ex, "Lifecycle sweep failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
