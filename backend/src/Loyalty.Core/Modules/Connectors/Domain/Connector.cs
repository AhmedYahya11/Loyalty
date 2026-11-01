using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Connectors.Domain;

/// <summary>Supported e-commerce / POS platforms (B2). Shopify is the first connector.</summary>
public enum ConnectorPlatform { Shopify = 0 }

/// <summary>
/// A per-tenant inbound connector (B2). Holds the HMAC shared secret used to authenticate the
/// platform's webhooks. Application-scoped — the connector id in the webhook URL resolves to exactly
/// one tenant, and events are ingested only within that application (tenant isolation).
///
/// The secret is stored recoverable (HMAC verification needs the raw key); encrypt at rest in prod.
/// </summary>
public sealed class Connector : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public ConnectorPlatform Platform { get; private set; }
    public string Secret { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Connector() { }

    public static Connector Create(Guid applicationId, ConnectorPlatform platform, string secret, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ApplicationId = applicationId,
        Platform = platform,
        Secret = secret,
        IsActive = true,
        CreatedAt = now,
    };

    public void SetActive(bool active) => IsActive = active;
}
