using System.Net.Http;
using System.Text;
using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Notifications.Application;

/// <summary>
/// Delivers undelivered event-log entries to active webhook subscriptions (HTTP POST + HMAC),
/// marking them delivered on success. Runs in system scope so it can sweep every application.
/// A simple at-least-once delivery with bounded attempts (receivers must be idempotent).
/// </summary>
public sealed class WebhookDispatcher(
    IRepository<Loyalty.Core.Modules.Notifications.Domain.EventLogEntry> eventLogRepo,
    IRepository<Loyalty.Core.Modules.Notifications.Domain.WebhookSubscription> webhookSubscriptionsRepo,
    IUnitOfWork uow, ApplicationContext scope, IHttpClientFactory httpClientFactory, IClock clock)
{
    private const int MaxAttempts = 5;
    private const int BatchSize = 50;

    public async Task<int> DispatchPendingAsync(CancellationToken ct)
    {
        scope.EnterSystemScope();
        try
        {
            var pending = await eventLogRepo.Query()
                .Where(e => e.DeliveredAt == null && e.DeliveryAttempts < MaxAttempts)
                .OrderBy(e => e.CreatedAt).Take(BatchSize).ToListAsync(ct);
            if (pending.Count == 0) return 0;

            foreach (var entry in pending)
            {
                var targets = await webhookSubscriptionsRepo.Query()
                    .Where(s => s.ApplicationId == entry.ApplicationId && s.IsActive).ToListAsync(ct);
                targets = targets.Where(s => s.Accepts(entry.EventType)).ToList();

                if (targets.Count == 0) { entry.MarkDelivered(clock.UtcNow); continue; }

                var allOk = true;
                foreach (var sub in targets)
                {
                    try
                    {
                        var client = httpClientFactory.CreateClient("webhook");
                        client.Timeout = TimeSpan.FromSeconds(10);
                        using var req = new HttpRequestMessage(HttpMethod.Post, sub.Url)
                        {
                            Content = new StringContent(entry.Payload, Encoding.UTF8, "application/json"),
                        };
                        req.Headers.TryAddWithoutValidation("X-Loyalty-Event", entry.EventType);
                        req.Headers.TryAddWithoutValidation("X-Loyalty-Signature", WebhookSigner.Sign(sub.Secret, entry.Payload));
                        var resp = await client.SendAsync(req, ct);
                        if (!resp.IsSuccessStatusCode) allOk = false;
                    }
                    catch { allOk = false; }
                }

                if (allOk) entry.MarkDelivered(clock.UtcNow);
                else entry.MarkAttempted();
            }

            await uow.SaveChangesAsync(ct);
            return pending.Count;
        }
        finally { scope.LeaveSystemScope(); }
    }
}
