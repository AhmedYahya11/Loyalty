using System.Text.Json;
using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Notifications.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Notifications.Application;

public sealed record EventLogView(Guid Id, string EventType, Guid? MemberId, Guid? RequestId, JsonElement Payload, DateTimeOffset OccurredAt, DateTimeOffset? DeliveredAt);
public sealed record WebhookView(Guid Id, string Url, IReadOnlyList<string> EventTypes, bool IsActive, DateTimeOffset CreatedAt);
public sealed record WebhookWithSecret(Guid Id, string Url, string Secret, IReadOnlyList<string> EventTypes, bool IsActive);
public sealed record CreateWebhookInput(string Url, string? Secret, IReadOnlyList<string>? EventTypes);

/// <summary>Reads the result-event log and manages webhook subscriptions (application-scoped).</summary>
public sealed class NotificationService(
    IRepository<Loyalty.Core.Modules.Notifications.Domain.EventLogEntry> eventLogRepo,
    IRepository<Loyalty.Core.Modules.Notifications.Domain.WebhookSubscription> webhookSubscriptionsRepo,
    IUnitOfWork uow, ApplicationContext scope, IClock clock)
{
    public async Task<IReadOnlyList<EventLogView>> ListEventsAsync(int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 200);
        var rows = await eventLogRepo.Query().OrderByDescending(e => e.CreatedAt).Take(limit).ToListAsync(ct);
        return rows.Select(e => new EventLogView(e.Id, e.EventType, e.MemberId, e.RequestId,
            JsonDocument.Parse(e.Payload).RootElement.Clone(), e.OccurredAt, e.DeliveredAt)).ToList();
    }

    public async Task<WebhookWithSecret> CreateWebhookAsync(CreateWebhookInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Url) || !Uri.TryCreate(input.Url, UriKind.Absolute, out _))
            throw new LoyaltyException(LoyaltyError.ValidationFailed("A valid absolute url is required."));
        var secret = string.IsNullOrWhiteSpace(input.Secret) ? WebhookSigner.NewSecret() : input.Secret!;
        var sub = WebhookSubscription.Create(scope.ApplicationId, input.Url, secret, input.EventTypes, clock.UtcNow);
        webhookSubscriptionsRepo.Add(sub);
        await uow.SaveChangesAsync(ct);
        return new WebhookWithSecret(sub.Id, sub.Url, sub.Secret, sub.EventTypes, sub.IsActive);
    }

    public async Task<IReadOnlyList<WebhookView>> ListWebhooksAsync(CancellationToken ct) =>
        await webhookSubscriptionsRepo.Query().OrderByDescending(w => w.CreatedAt)
            .Select(w => new WebhookView(w.Id, w.Url, w.EventTypes, w.IsActive, w.CreatedAt)).ToListAsync(ct);

    public async Task<bool> SetWebhookActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var w = await webhookSubscriptionsRepo.Query().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (w is null) return false;
        w.SetActive(active);
        await uow.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteWebhookAsync(Guid id, CancellationToken ct)
    {
        var w = await webhookSubscriptionsRepo.Query().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (w is null) return false;
        webhookSubscriptionsRepo.Remove(w);
        await uow.SaveChangesAsync(ct);
        return true;
    }
}
