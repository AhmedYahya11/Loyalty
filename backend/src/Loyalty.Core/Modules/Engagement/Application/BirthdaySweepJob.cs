using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Loyalty.Core.Modules.Engagement.Application;

/// <summary>
/// Hosted background job that runs the daily birthday sweep. The first run is delayed so it never
/// interferes with request-time processing or tests; thereafter it runs once per day. Mirrors
/// <c>LifecycleSweepJob</c>.
/// </summary>
public sealed class BirthdaySweepJob(IServiceScopeFactory scopeFactory, ILogger<BirthdaySweepJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromHours(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(InitialDelay, stoppingToken); } catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var sweep = scope.ServiceProvider.GetRequiredService<BirthdaySweepService>();
                var n = await sweep.RunAsync(stoppingToken);
                logger.LogInformation("Birthday sweep complete: {Rewarded} member(s) rewarded", n);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.LogError(ex, "Birthday sweep failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
