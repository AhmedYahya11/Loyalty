using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Loyalty.Core.Modules.Cvm.Application;

/// <summary>
/// Hosted background job that recomputes RFM personas daily (P12). First run is delayed so it never
/// interferes with request-time processing or tests; thereafter once per day. Mirrors the lifecycle
/// and birthday sweeps.
/// </summary>
public sealed class RfmJob(IServiceScopeFactory scopeFactory, ILogger<RfmJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromHours(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(InitialDelay, stoppingToken); } catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var rfm = scope.ServiceProvider.GetRequiredService<RfmService>();
                var n = await rfm.RunAsync(stoppingToken);
                logger.LogInformation("RFM recompute complete: {Members} member(s) scored", n);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.LogError(ex, "RFM recompute failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
