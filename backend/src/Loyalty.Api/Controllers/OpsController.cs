using System.Text.Json;
using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Analytics;
using Loyalty.Core.Modules.Cvm.Application;
using Loyalty.Core.Modules.Integration.Application;
using Loyalty.Core.Modules.Integration.Contracts;
using Loyalty.Core.Modules.Notifications.Application;
using Loyalty.Core.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Loyalty.Api.Controllers;

/// <summary>Generic event ingestion and request (processing) status.</summary>
[Tags("Events")]
public sealed class EventsController(EventIngestionService svc) : LoyaltyControllerBase
{
    [HttpPost("/api/v1/events")]
    [EnableRateLimiting("events")]
    public async Task<IActionResult> Ingest(
        [FromBody] LoyaltyEventEnvelopeV1 envelope,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var canonical = JsonSerializer.Serialize(envelope, JsonOptionsProvider.Default);
        var result = await svc.SubmitAsync(envelope, canonical, idempotencyKey, ct);
        return StatusCode(result.StatusCode, result.Body);
    }

    [HttpGet("/api/v1/requests/{id:guid}")]
    public async Task<IActionResult> GetRequest(Guid id, CancellationToken ct) => OkOrNotFound(await svc.GetRequestStatusAsync(id, ct));

    [HttpGet("/api/v1/requests")]
    public async Task<IActionResult> ListRequests([FromQuery] string? status, [FromQuery] int? limit, CancellationToken ct) =>
        Ok(new { items = await svc.ListRequestsAsync(status, limit ?? 50, ct) });

    [HttpPost("/api/v1/requests/{id:guid}/replay")]
    public async Task<IActionResult> Replay(Guid id, CancellationToken ct) => OkOrNotFound(await svc.ReplayAsync(id, ct));
}

/// <summary>Reporting: overview, promotions, and RFM persona distribution.</summary>
[Tags("Analytics")]
[Route("api/v1/analytics")]
public sealed class AnalyticsController(AnalyticsService analytics, PromotionAnalyticsService promotions, RfmService rfm) : LoyaltyControllerBase
{
    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken ct) => Ok(await analytics.OverviewAsync(ct));

    [HttpGet("promotions")]
    public async Task<IActionResult> Promotions(CancellationToken ct) => Ok(await promotions.SummaryAsync(ct));

    [HttpGet("rfm")]
    public async Task<IActionResult> Rfm(CancellationToken ct) => Ok(await rfm.SummaryAsync(ct));
}

/// <summary>Result-event feed for the dashboard.</summary>
[Tags("Notifications")]
public sealed class NotificationsController(NotificationService svc) : LoyaltyControllerBase
{
    [HttpGet("/api/v1/notifications")]
    public async Task<IActionResult> List([FromQuery] int? limit, CancellationToken ct) =>
        Ok(new { items = await svc.ListEventsAsync(limit ?? 50, ct) });
}

/// <summary>HMAC-signed webhook endpoints and on-demand delivery.</summary>
[Tags("Webhooks")]
[Route("api/v1/webhooks")]
public sealed class WebhooksController(NotificationService svc, WebhookDispatcher dispatcher) : LoyaltyControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWebhookInput req, CancellationToken ct) => Created201(await svc.CreateWebhookAsync(req, ct));

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await svc.ListWebhooksAsync(ct));

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] SetStatusRequest req, CancellationToken ct) =>
        NoContentIf(await svc.SetWebhookActiveAsync(id, req.IsActive, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) => NoContentIf(await svc.DeleteWebhookAsync(id, ct));

    // on-demand delivery (ops/testing)
    [HttpPost("dispatch")]
    public async Task<IActionResult> Dispatch(CancellationToken ct) => Ok(new { dispatched = await dispatcher.DispatchPendingAsync(ct) });
}
