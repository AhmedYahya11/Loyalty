using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;

namespace Loyalty.Tests.Integration;

/// <summary>
/// Phase-1 release gate: bootstrap → configure loyalty settings → create member + active
/// purchase-earn campaign → submit a Hareer-shaped PurchaseCompleted event → points land in
/// the wallet. Also proves HTTP idempotency (same key → same request, no double award).
/// </summary>
[Collection("api")]
public class PurchaseEarnE2ETests(ApiFixture fx)
{
    [Fact]
    public async Task Purchase_event_earns_points_end_to_end()
    {
        var http = fx.Client;

        // 1. bootstrap root application + root account
        var bootstrap = await PostJson(http, "/api/v1/bootstrap",
            new { name = "Hareer", nameAr = "حرير" },
            headers: new() { ["X-Bootstrap-Token"] = ApiFixture.BootstrapToken });
        bootstrap.status.ShouldBe(HttpStatusCode.Created);
        var apiKey = bootstrap.json.GetProperty("rootAccount").GetProperty("apiKey").GetString()!;
        var appId = bootstrap.json.GetProperty("application").GetProperty("id").GetString()!;
        var auth = new Dictionary<string, string> { ["X-Api-Key"] = apiKey };

        // 2. loyalty settings: 1000 IQD per point, Floor, Active
        var settings = await PutJson(http, $"/api/v1/applications/{appId}/loyalty-settings", new
        {
            pointsUnitCode = "POINT",
            baseCurrency = "IQD",
            spendAmountPerPoint = 1000,
            roundingPolicy = "Floor",
            loyaltyStatus = "Active",
        }, auth);
        settings.status.ShouldBe(HttpStatusCode.OK);

        // 3. member
        var member = await PostJson(http, "/api/v1/members", new { externalCustomerId = "user-1" }, auth);
        member.status.ShouldBe(HttpStatusCode.Created);
        var memberId = member.json.GetProperty("memberId").GetString()!;

        // 4. purchase-earn campaign (base points), then activate
        var campaign = await PostJson(http, "/api/v1/campaigns", new
        {
            name = "Base Earn",
            nameAr = "نقاط الشراء",
            triggerEventType = "PurchaseCompleted",
            minimumEventVersion = 1,
            priority = 100,
            stackingPolicy = "Stackable",
            conditions = Array.Empty<object>(),
            effects = new[] { new { effectId = "base-earn", type = "AwardBasePoints" } },
        }, auth);
        campaign.status.ShouldBe(HttpStatusCode.Created);
        var campaignId = campaign.json.GetProperty("id").GetString()!;
        (await PostJson(http, $"/api/v1/campaigns/{campaignId}/activate", new { }, auth)).status.ShouldBe(HttpStatusCode.OK);

        // 5. submit a Hareer-shaped PurchaseCompleted event (50,000 IQD → 50 points)
        var envelope = new
        {
            eventId = Guid.NewGuid(),
            eventType = "PurchaseCompleted",
            eventVersion = 1,
            sourceTransactionId = "ORDER-1",
            customerId = "user-1",
            occurredAt = DateTimeOffset.UtcNow,
            correlationId = "corr-1",
            attributes = new { orderId = "ORDER-1", amount = 50000, currency = "IQD" },
        };
        var submit = await PostJson(http, "/api/v1/events", envelope,
            new() { ["X-Api-Key"] = apiKey, ["Idempotency-Key"] = "order-1-key" });
        submit.status.ShouldBe(HttpStatusCode.Accepted);
        var requestId = submit.json.GetProperty("requestId").GetString()!;

        // 6. wait for async processing to complete
        var finalStatus = await PollStatus(http, requestId, auth);
        finalStatus.GetProperty("status").GetString().ShouldBe("Completed");
        finalStatus.GetProperty("awardedPoints").GetInt64().ShouldBe(50);

        // 7. wallet reflects the earned points
        var wallet = await GetJson(http, $"/api/v1/members/{memberId}/wallet", auth);
        wallet.json.GetProperty("activeBalance").GetInt64().ShouldBe(50);

        // 8. idempotency: same key → same request id, no double award
        var replay = await PostJson(http, "/api/v1/events", envelope,
            new() { ["X-Api-Key"] = apiKey, ["Idempotency-Key"] = "order-1-key" });
        replay.status.ShouldBe(HttpStatusCode.Accepted);
        replay.json.GetProperty("requestId").GetString().ShouldBe(requestId);

        var wallet2 = await GetJson(http, $"/api/v1/members/{memberId}/wallet", auth);
        wallet2.json.GetProperty("activeBalance").GetInt64().ShouldBe(50);
    }

    // ---- helpers ----

    private static async Task<JsonElement> PollStatus(HttpClient http, string requestId, Dictionary<string, string> auth)
    {
        for (var i = 0; i < 40; i++)
        {
            var r = await GetJson(http, $"/api/v1/requests/{requestId}", auth);
            var status = r.json.GetProperty("status").GetString();
            if (status is "Completed" or "Rejected" or "Failed") return r.json;
            await Task.Delay(500);
        }
        throw new TimeoutException($"Request {requestId} did not reach a terminal status.");
    }

    private static async Task<(HttpStatusCode status, JsonElement json)> PostJson(
        HttpClient http, string url, object body, Dictionary<string, string>? headers = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        AddHeaders(req, headers);
        return await Send(http, req);
    }

    private static async Task<(HttpStatusCode status, JsonElement json)> PutJson(
        HttpClient http, string url, object body, Dictionary<string, string>? headers = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) };
        AddHeaders(req, headers);
        return await Send(http, req);
    }

    private static async Task<(HttpStatusCode status, JsonElement json)> GetJson(
        HttpClient http, string url, Dictionary<string, string>? headers = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddHeaders(req, headers);
        return await Send(http, req);
    }

    private static void AddHeaders(HttpRequestMessage req, Dictionary<string, string>? headers)
    {
        if (headers is null) return;
        foreach (var (k, v) in headers) req.Headers.TryAddWithoutValidation(k, v);
    }

    private static async Task<(HttpStatusCode, JsonElement)> Send(HttpClient http, HttpRequestMessage req)
    {
        var resp = await http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        var json = string.IsNullOrWhiteSpace(text)
            ? default
            : JsonDocument.Parse(text).RootElement.Clone();
        return (resp.StatusCode, json);
    }
}
