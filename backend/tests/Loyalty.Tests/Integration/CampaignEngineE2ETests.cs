using Shouldly;

namespace Loyalty.Tests.Integration;

/// <summary>
/// Phase-3 campaign engine: member limits cap repeated rewards, global budget stops awards when
/// exhausted, and a refund reverses the original earn.
/// </summary>
[Collection("api")]
public class CampaignEngineE2ETests(ApiFixture fx)
{
    private async Task<(string appId, Dictionary<string, string> auth)> SetupApp(string name)
    {
        var (appId, apiKey) = await fx.OnboardChildAsync(name);
        var auth = ApiFixture.Auth(apiKey);
        await fx.Put($"/api/v1/applications/{appId}/loyalty-settings", new
        {
            pointsUnitCode = "POINT", baseCurrency = "IQD", spendAmountPerPoint = 1000,
            roundingPolicy = "Floor", loyaltyStatus = "Active",
        }, auth);
        await fx.Post("/api/v1/members", new { externalCustomerId = "c1" }, auth);
        return (appId, auth);
    }

    private static object Purchase(string orderId, int amount) => new
    {
        eventId = Guid.NewGuid(), eventType = "PurchaseCompleted", eventVersion = 1,
        sourceTransactionId = orderId, customerId = "c1", occurredAt = DateTimeOffset.UtcNow,
        correlationId = orderId, attributes = new { orderId, amount, currency = "IQD" },
    };

    [Fact]
    public async Task Member_daily_limit_caps_rewards()
    {
        var (_, auth) = await SetupApp("limit");
        var c = await fx.Post("/api/v1/campaigns", new
        {
            name = "Daily bonus", triggerEventType = "PurchaseCompleted", minimumEventVersion = 1,
            priority = 100, stackingPolicy = "Stackable", conditions = Array.Empty<object>(),
            effects = new[] { new { effectId = "bonus", type = "AwardPoints", points = 10 } },
            memberLimits = new[] { new { period = "CalendarDay", maximumExecutions = 1 } },
        }, auth);
        await fx.ActivateCampaign(c.GetProperty("id").GetString()!, auth);

        var r1 = await fx.Post("/api/v1/events", Purchase("ORDER-1", 1000), Sub(auth, "k1"));
        (await fx.PollRequest(r1.GetProperty("requestId").GetString()!, auth)).GetProperty("awardedPoints").GetInt64().ShouldBe(10);

        var r2 = await fx.Post("/api/v1/events", Purchase("ORDER-2", 1000), Sub(auth, "k2"));
        var s2 = await fx.PollRequest(r2.GetProperty("requestId").GetString()!, auth);
        s2.GetProperty("status").GetString().ShouldBe("Rejected"); // daily limit reached

        var wallet = await fx.Get("/api/v1/members/by-external-id/c1", auth);
        var memberId = wallet.GetProperty("memberId").GetString()!;
        (await fx.Get($"/api/v1/members/{memberId}/wallet", auth)).GetProperty("activeBalance").GetInt64().ShouldBe(10);
    }

    [Fact]
    public async Task Global_budget_exhaustion_stops_awards()
    {
        var (_, auth) = await SetupApp("budget");
        var c = await fx.Post("/api/v1/campaigns", new
        {
            name = "Budgeted", triggerEventType = "PurchaseCompleted", minimumEventVersion = 1,
            priority = 100, stackingPolicy = "Stackable", conditions = Array.Empty<object>(),
            effects = new[] { new { effectId = "bonus", type = "AwardPoints", points = 10 } },
            globalBudget = 15,
        }, auth);
        await fx.ActivateCampaign(c.GetProperty("id").GetString()!, auth);

        var r1 = await fx.Post("/api/v1/events", Purchase("B-1", 1000), Sub(auth, "b1"));
        (await fx.PollRequest(r1.GetProperty("requestId").GetString()!, auth)).GetProperty("awardedPoints").GetInt64().ShouldBe(10);

        var r2 = await fx.Post("/api/v1/events", Purchase("B-2", 1000), Sub(auth, "b2"));
        (await fx.PollRequest(r2.GetProperty("requestId").GetString()!, auth)).GetProperty("status").GetString().ShouldBe("Rejected");

        var m = await fx.Get("/api/v1/members/by-external-id/c1", auth);
        (await fx.Get($"/api/v1/members/{m.GetProperty("memberId").GetString()}/wallet", auth))
            .GetProperty("activeBalance").GetInt64().ShouldBe(10);
    }

    [Fact]
    public async Task Refund_reverses_the_original_earn()
    {
        var (_, auth) = await SetupApp("refund");
        var c = await fx.Post("/api/v1/campaigns", new
        {
            name = "Base Earn", triggerEventType = "PurchaseCompleted", minimumEventVersion = 1,
            priority = 100, stackingPolicy = "Stackable", conditions = Array.Empty<object>(),
            effects = new[] { new { effectId = "base-earn", type = "AwardBasePoints" } },
        }, auth);
        await fx.ActivateCampaign(c.GetProperty("id").GetString()!, auth);

        var m = await fx.Get("/api/v1/members/by-external-id/c1", auth);
        var memberId = m.GetProperty("memberId").GetString()!;

        var buy = await fx.Post("/api/v1/events", Purchase("ORDER-9", 50000), Sub(auth, "buy9"));
        (await fx.PollRequest(buy.GetProperty("requestId").GetString()!, auth)).GetProperty("awardedPoints").GetInt64().ShouldBe(50);
        (await fx.Get($"/api/v1/members/{memberId}/wallet", auth)).GetProperty("activeBalance").GetInt64().ShouldBe(50);

        // refund the order → reverse the 50 points
        var refund = await fx.Post("/api/v1/events", new
        {
            eventId = Guid.NewGuid(), eventType = "OrderRefunded", eventVersion = 1,
            sourceTransactionId = "REFUND-9", customerId = "c1", occurredAt = DateTimeOffset.UtcNow,
            correlationId = "r9", attributes = new { refundId = "REFUND-9", originalOrderId = "ORDER-9", amount = 50000, currency = "IQD" },
        }, Sub(auth, "refund9"));
        (await fx.PollRequest(refund.GetProperty("requestId").GetString()!, auth)).GetProperty("status").GetString().ShouldBe("Completed");

        (await fx.Get($"/api/v1/members/{memberId}/wallet", auth)).GetProperty("activeBalance").GetInt64().ShouldBe(0);
        (await fx.Get($"/api/v1/members/{memberId}/value-profile", auth)).GetProperty("netSpendAmount").GetDecimal().ShouldBe(0);
    }

    // reuse the test's api key and add an idempotency key for event submission
    private static Dictionary<string, string> Sub(Dictionary<string, string> auth, string k) =>
        new() { ["X-Api-Key"] = auth["X-Api-Key"], ["Idempotency-Key"] = k };
}
