using Loyalty.Core.Modules.Connectors.Application;
using Microsoft.AspNetCore.Mvc;

namespace Loyalty.Api.Controllers;

/// <summary>
/// Inbound e-commerce / POS connectors (B2). Admins manage per-tenant connectors; platforms post signed
/// webhooks to the connector URL. The webhook route is authenticated by HMAC (not API key), so it is on
/// the anonymous path list — all auth + tenant isolation happen inside the service.
/// </summary>
[Tags("Connectors")]
[Route("api/v1/connectors")]
public sealed class ConnectorsController(ConnectorService connectors) : LoyaltyControllerBase
{
    /// <summary>Create a connector; the HMAC secret is returned once, here.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateConnectorRequest req, CancellationToken ct) =>
        Created201(await connectors.CreateAsync(req.Platform, req.Secret, ct));

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await connectors.ListAsync(ct));

    /// <summary>Shopify webhook receiver (orders/paid, refunds/create). Authenticated by HMAC.</summary>
    [HttpPost("/api/v1/connectors/shopify/{connectorId:guid}/webhook")]
    public async Task<IActionResult> ShopifyWebhook(Guid connectorId, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, ct);
        var raw = ms.ToArray();
        var result = await connectors.HandleShopifyWebhookAsync(
            connectorId,
            Request.Headers["X-Shopify-Topic"].ToString(),
            Request.Headers["X-Shopify-Webhook-Id"].ToString(),
            raw,
            Request.Headers["X-Shopify-Hmac-Sha256"].ToString(),
            ct);
        return StatusCode(result.StatusCode, result.Body);
    }
}
