using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Loyalty.Core.Modules.Notifications.Application;

/// <summary>Periodically delivers pending webhook events (every few seconds, after a short delay).</summary>
public sealed class WebhookDispatcherJob(IServiceScopeFactory scopeFactory, ILogger<WebhookDispatcherJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); } catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<WebhookDispatcher>();
                await dispatcher.DispatchPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.LogError(ex, "Webhook dispatch failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
