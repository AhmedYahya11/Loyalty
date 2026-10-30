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
public sealed class NotificationService(LoyaltyDbContext db, ApplicationContext scope, IClock clock)
{
    public async Task<IReadOnlyList<EventLogView>> ListEventsAsync(int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 200);
        var rows = await db.EventLog.OrderByDescending(e => e.CreatedAt).Take(limit).ToListAsync(ct);
        return rows.Select(e => new EventLogView(e.Id, e.EventType, e.MemberId, e.RequestId,
            JsonDocument.Parse(e.Payload).RootElement.Clone(), e.OccurredAt, e.DeliveredAt)).ToList();
    }

    public async Task<WebhookWithSecret> CreateWebhookAsync(CreateWebhookInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Url) || !Uri.TryCreate(input.Url, UriKind.Absolute, out _))
            throw new LoyaltyException(LoyaltyError.ValidationFailed("A valid absolute url is required."));
        var secret = string.IsNullOrWhiteSpace(input.Secret) ? WebhookSigner.NewSecret() : input.Secret!;
        var sub = WebhookSubscription.Create(scope.ApplicationId, input.Url, secret, input.EventTypes, clock.UtcNow);
        db.WebhookSubscriptions.Add(sub);
        await db.SaveChangesAsync(ct);
        return new WebhookWithSecret(sub.Id, sub.Url, sub.Secret, sub.EventTypes, sub.IsActive);
    }

    public async Task<IReadOnlyList<WebhookView>> ListWebhooksAsync(CancellationToken ct) =>
        await db.WebhookSubscriptions.OrderByDescending(w => w.CreatedAt)
            .Select(w => new WebhookView(w.Id, w.Url, w.EventTypes, w.IsActive, w.CreatedAt)).ToListAsync(ct);

    public async Task<bool> SetWebhookActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var w = await db.WebhookSubscriptions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (w is null) return false;
        w.SetActive(active);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteWebhookAsync(Guid id, CancellationToken ct)
    {
        var w = await db.WebhookSubscriptions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (w is null) return false;
        db.WebhookSubscriptions.Remove(w);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
