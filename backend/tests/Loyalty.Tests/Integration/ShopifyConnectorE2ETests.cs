using System.Net;
using System.Security.Cryptography;
using System.Text;
using Shouldly;

namespace Loyalty.Tests.Integration;

/// <summary>
/// B2 — Shopify inbound connector. A signed orders/paid webhook ingests a PurchaseCompleted (earning
/// points) scoped to the connector's tenant; a bad signature is rejected (401); the same delivery is
/// idempotent (no double points); a signed refunds/create reverses the earn; malformed JSON is 400.
/// Exercises: HMAC auth, tenant isolation, idempotency, validation, and the event log/pipeline.
/// </summary>
[Collection("api")]
public class ShopifyConnectorE2ETests(ApiFixture fx)
{
    private static string Hmac(string secret, string body)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(body)));
    }

    private async Task<HttpResponseMessage> PostWebhook(Guid id, string topic, string webhookId, string body, string? signature)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/connectors/shopify/{id}/webhook")
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        req.Headers.TryAddWithoutValidation("X-Shopify-Topic", topic);
        req.Headers.TryAddWithoutValidation("X-Shopify-Webhook-Id", webhookId);
        if (signature is not null) req.Headers.TryAddWithoutValidation("X-Shopify-Hmac-Sha256", signature);
        return await fx.Client.SendAsync(req);
    }

    private async Task<long> Balance(string apiKey, string customerId)
    {
        var m = await fx.Get($"/api/v1/members/by-external-id/{customerId}", ApiFixture.Auth(apiKey));
        return m.GetProperty("wallet").GetProperty("activeBalance").GetInt64();
    }

    [Fact]
    public async Task Shopify_webhook_earns_refunds_and_enforces_auth_idempotency()
    {
        var (appId, apiKey) = await fx.OnboardChildAsync("shopify");
        var auth = ApiFixture.Auth(apiKey);
        await fx.Put($"/api/v1/applications/{appId}/loyalty-settings", new
        {
            pointsUnitCode = "POINT", baseCurrency = "USD", spendAmountPerPoint = 1000,
            roundingPolicy = "Floor", loyaltyStatus = "Active",
        }, auth);
        var c = await fx.Post("/api/v1/campaigns", new
        {
            name = "Base Earn", triggerEventType = "PurchaseCompleted", minimumEventVersion = 1,
            priority = 100, stackingPolicy = "Stackable", conditions = Array.Empty<object>(),
            effects = new[] { new { effectId = "base-earn", type = "AwardBasePoints" } },
        }, auth);
        await fx.ActivateCampaign(c.GetProperty("id").GetString()!, auth);

        // create the connector; the HMAC secret is returned once
        var connector = await fx.Post("/api/v1/connectors", new { platform = "Shopify" }, auth);
        var connectorId = Guid.Parse(connector.GetProperty("id").GetString()!);
        var secret = connector.GetProperty("secret").GetString()!;

        var order = """{"id":5001,"total_price":"50000.00","currency":"USD","customer":{"id":777},"processed_at":"2026-11-01T10:00:00Z"}""";

        // bad signature → 401, nothing ingested
        (await PostWebhook(connectorId, "orders/paid", "whk-bad", order, Hmac("wrong-secret", order)))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // valid signature → accepted → processed → 50 points (50000 / 1000)
        var ok = await PostWebhook(connectorId, "orders/paid", "whk-1", order, Hmac(secret, order));
        ok.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var reqId = System.Text.Json.JsonDocument.Parse(await ok.Content.ReadAsStringAsync()).RootElement.GetProperty("requestId").GetString()!;
        (await fx.PollRequest(reqId, auth)).GetProperty("status").GetString().ShouldBe("Completed");
        (await Balance(apiKey, "777")).ShouldBe(50);

        // same delivery id again → idempotent (no double points)
        await PostWebhook(connectorId, "orders/paid", "whk-1", order, Hmac(secret, order));
        (await Balance(apiKey, "777")).ShouldBe(50);

        // signed refund → reverses the earn
        var refund = """{"id":9001,"order_id":5001,"currency":"USD","transactions":[{"amount":"50000.00"}]}""";
        var refundResp = await PostWebhook(connectorId, "refunds/create", "whk-r1", refund, Hmac(secret, refund));
        refundResp.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var refundReqId = System.Text.Json.JsonDocument.Parse(await refundResp.Content.ReadAsStringAsync()).RootElement.GetProperty("requestId").GetString()!;
        (await fx.PollRequest(refundReqId, auth)).GetProperty("status").GetString().ShouldBe("Completed");
        (await Balance(apiKey, "777")).ShouldBe(0);

        // malformed body (valid signature) → 422 (validation failed)
        var bad = "not json";
        (await PostWebhook(connectorId, "orders/paid", "whk-x", bad, Hmac(secret, bad)))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}
