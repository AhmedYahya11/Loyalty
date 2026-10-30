using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Notifications.Domain;

/// <summary>
/// An application-scoped webhook endpoint. Result events are POSTed to <see cref="Url"/> with an
/// HMAC-SHA256 signature over the body using <see cref="Secret"/>. EventTypes empty = all types.
/// </summary>
public sealed class WebhookSubscription : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public string Url { get; private set; } = default!;
    public string Secret { get; private set; } = default!;
    public List<string> EventTypes { get; private set; } = [];
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private WebhookSubscription() { }

    public static WebhookSubscription Create(Guid applicationId, string url, string secret, IEnumerable<string>? eventTypes, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ApplicationId = applicationId,
        Url = url.Trim(),
        Secret = secret,
        EventTypes = eventTypes?.ToList() ?? [],
        IsActive = true,
        CreatedAt = now,
    };

    public void SetActive(bool active) => IsActive = active;

    public bool Accepts(string eventType) => EventTypes.Count == 0 || EventTypes.Contains(eventType);
}
