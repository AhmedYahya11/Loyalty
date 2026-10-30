using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace Loyalty.Tests.Integration;

/// <summary>
/// Phase-5 ops: reconciliation agrees with the ledger, readiness health is green, dead-letter
/// replay guards terminal requests, and event ingestion is rate-limited per API key.
/// </summary>
[Collection("api")]
public class OpsE2ETests(ApiFixture fx)
{
    private async Task<(Dictionary<string, string> auth, string memberId)> EarningApp(string name)
    {
        var (appId, apiKey) = await fx.OnboardChildAsync(name);
        var auth = ApiFixture.Auth(apiKey);
        await fx.Put($"/api/v1/applications/{appId}/loyalty-settings", new
        {
            pointsUnitCode = "POINT", baseCurrency = "IQD", spendAmountPerPoint = 1000,
            roundingPolicy = "Floor", loyaltyStatus = "Active",
        }, auth);
        var m = await fx.Post("/api/v1/members", new { externalCustomerId = "c1" }, auth);
        var c = await fx.Post("/api/v1/campaigns", new
        {
            name = "Base Earn", triggerEventType = "PurchaseCompleted", minimumEventVersion = 1,
            priority = 100, stackingPolicy = "Stackable", conditions = Array.Empty<object>(),
            effects = new[] { new { effectId = "base-earn", type = "AwardBasePoints" } },
        }, auth);
        await fx.ActivateCampaign(c.GetProperty("id").GetString()!, auth);
        return (auth, m.GetProperty("memberId").GetString()!);
    }

    private static Dictionary<string, string> Sub(Dictionary<string, string> auth, string k) =>
        new() { ["X-Api-Key"] = auth["X-Api-Key"], ["Idempotency-Key"] = k };

    [Fact]
    public async Task Reconciliation_agrees_with_ledger()
    {
        var (auth, memberId) = await EarningApp("ops-recon");
        var r = await fx.Post("/api/v1/events", new
        {
            eventId = Guid.NewGuid(), eventType = "PurchaseCompleted", eventVersion = 1,
            sourceTransactionId = "O1", customerId = "c1", occurredAt = DateTimeOffset.UtcNow,
            correlationId = "o1", attributes = new { orderId = "O1", amount = 50000, currency = "IQD" },
        }, Sub(auth, "recon-1"));
        await fx.PollRequest(r.GetProperty("requestId").GetString()!, auth);

        var report = await fx.Post($"/api/v1/members/{memberId}/reconcile", new { }, auth);
        report.GetProperty("balanced").GetBoolean().ShouldBeTrue();
        report.GetProperty("active").GetProperty("stored").GetInt64().ShouldBe(50);
        report.GetProperty("active").GetProperty("computed").GetInt64().ShouldBe(50);
        report.GetProperty("pointsEarned").GetProperty("match").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Health_ready_returns_200()
    {
        var resp = await fx.Client.GetAsync("/health/ready");
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Replay_of_completed_request_is_conflict()
    {
        var (auth, _) = await EarningApp("ops-replay");
        var r = await fx.Post("/api/v1/events", new
        {
            eventId = Guid.NewGuid(), eventType = "PurchaseCompleted", eventVersion = 1,
            sourceTransactionId = "O2", customerId = "c1", occurredAt = DateTimeOffset.UtcNow,
            correlationId = "o2", attributes = new { orderId = "O2", amount = 1000, currency = "IQD" },
        }, Sub(auth, "replay-1"));
        var requestId = r.GetProperty("requestId").GetString()!;
        await fx.PollRequest(requestId, auth);

        // list includes the completed request
        var list = await fx.Get("/api/v1/requests?status=Completed", auth);
        list.GetProperty("items").GetArrayLength().ShouldBeGreaterThan(0);

        // replaying a finished request is rejected
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/requests/{requestId}/replay");
        req.Headers.TryAddWithoutValidation("X-Api-Key", auth["X-Api-Key"]);
        var resp = await fx.Client.SendAsync(req);
        resp.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Event_ingestion_is_rate_limited()
    {
        var (auth, _) = await EarningApp("ops-rate");

        async Task<HttpStatusCode> Send(int i)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/events")
            {
                Content = JsonContent.Create(new
                {
                    eventId = Guid.NewGuid(), eventType = "PurchaseCompleted", eventVersion = 1,
                    sourceTransactionId = $"RL-{i}", customerId = "c1", occurredAt = DateTimeOffset.UtcNow,
                    correlationId = $"rl-{i}", attributes = new { orderId = $"RL-{i}", amount = 1000, currency = "IQD" },
                }),
            };
            req.Headers.TryAddWithoutValidation("X-Api-Key", auth["X-Api-Key"]);
            req.Headers.TryAddWithoutValidation("Idempotency-Key", $"rl-{i}");
            return (await fx.Client.SendAsync(req)).StatusCode;
        }

        // burst 60 concurrent with one key against a limit of 20/sec
        var results = await Task.WhenAll(Enumerable.Range(0, 60).Select(Send));
        results.ShouldContain(HttpStatusCode.TooManyRequests);
        results.ShouldContain(HttpStatusCode.Accepted);
    }
}
