using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Loyalty.Core.Modules.Wallets.Application;

/// <summary>
/// Hosted background job that expires stale points daily (P13). First run is delayed so it never
/// interferes with request-time processing or tests; thereafter once per day. Mirrors the other
/// daily sweeps.
/// </summary>
public sealed class PointsExpiryJob(IServiceScopeFactory scopeFactory, ILogger<PointsExpiryJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromHours(4);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(InitialDelay, stoppingToken); } catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<PointsExpiryService>();
                var n = await svc.RunAsync(stoppingToken);
                logger.LogInformation("Points expiry complete: {Expired} point(s) expired", n);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.LogError(ex, "Points expiry failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
