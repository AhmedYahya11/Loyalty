using Shouldly;

namespace Loyalty.Tests.Integration;

/// <summary>
/// Phase-14: a refund unwinds the stamp its purchase earned (and claws back a completion reward +
/// decrements a challenge's open progress), and the promotions report aggregates campaign usage,
/// reward redemptions, stamp completions and outstanding liabilities.
/// </summary>
[Collection("api")]
public class RefundReversalAndReportsE2ETests(ApiFixture fx)
{
    private static Dictionary<string, string> Sub(Dictionary<string, string> auth, string k) =>
        new() { ["X-Api-Key"] = auth["X-Api-Key"], ["Idempotency-Key"] = k };

    private static object Purchase(string orderId, string customer, int amount) => new
    {
        eventId = Guid.NewGuid(), eventType = "PurchaseCompleted", eventVersion = 1,
        sourceTransactionId = orderId, customerId = customer, occurredAt = DateTimeOffset.UtcNow,
        correlationId = orderId, attributes = new { orderId, amount, currency = "IQD" },
    };

    private static object Refund(string refundId, string originalOrderId, string customer, int amount) => new
    {
        eventId = Guid.NewGuid(), eventType = "OrderRefunded", eventVersion = 1,
        sourceTransactionId = refundId, customerId = customer, occurredAt = DateTimeOffset.UtcNow,
        correlationId = refundId, attributes = new { refundId, originalOrderId, amount, currency = "IQD" },
    };

    private async Task<Dictionary<string, string>> EarningApp(string name)
    {
        var (appId, apiKey) = await fx.OnboardChildAsync(name);
        var auth = ApiFixture.Auth(apiKey);
        await fx.Put($"/api/v1/applications/{appId}/loyalty-settings", new
        {
            pointsUnitCode = "POINT", baseCurrency = "IQD", spendAmountPerPoint = 1000, roundingPolicy = "Floor", loyaltyStatus = "Active",
        }, auth);
        var c = await fx.Post("/api/v1/campaigns", new
        {
            name = "Base Earn", triggerEventType = "PurchaseCompleted", minimumEventVersion = 1,
            priority = 100, stackingPolicy = "Stackable", conditions = Array.Empty<object>(),
            effects = new[] { new { effectId = "base-earn", type = "AwardBasePoints" } },
        }, auth);
        await fx.ActivateCampaign(c.GetProperty("id").GetString()!, auth);
        return auth;
    }

    [Fact]
    public async Task Refund_unwinds_the_stamp_and_claws_back_a_completed_card()
    {
        var auth = await EarningApp("refund-stamp");
        await fx.Post("/api/v1/stamp-cards", new { name = "Buy 2 get bonus", stampsRequired = 2, rewardPoints = 500, triggerEventType = "PurchaseCompleted" }, auth);

        // buy #1 (stamp 1), buy #2 (stamp 2 → completes, +500)
        await fx.PollRequest((await fx.Post("/api/v1/events", Purchase("B1", "dan", 1000), Sub(auth, "b1"))).GetProperty("requestId").GetString()!, auth);
        (await fx.PollRequest((await fx.Post("/api/v1/events", Purchase("B2", "dan", 1000), Sub(auth, "b2"))).GetProperty("requestId").GetString()!, auth))
            .GetProperty("awardedPoints").GetInt64().ShouldBe(501);

        var m = await fx.Get("/api/v1/members/by-external-id/dan", auth);
        var id = m.GetProperty("memberId").GetString()!;
        // 1 (B1) + 501 (B2) = 502 active
        (await fx.Get($"/api/v1/members/{id}/wallet", auth)).GetProperty("activeBalance").GetInt64().ShouldBe(502);
        (await fx.Get($"/api/v1/members/{id}/stamp-cards", auth)).GetProperty("items").EnumerateArray().First().GetProperty("completedCount").GetInt32().ShouldBe(1);

        // refund order B2 → reverse its 1 base point + claw back the 500 completion
        await fx.PollRequest((await fx.Post("/api/v1/events", Refund("RF2", "B2", "dan", 1000), Sub(auth, "rf2"))).GetProperty("requestId").GetString()!, auth);

        (await fx.Get($"/api/v1/members/{id}/wallet", auth)).GetProperty("activeBalance").GetInt64().ShouldBe(1); // only B1's point remains
        var card = (await fx.Get($"/api/v1/members/{id}/stamp-cards", auth)).GetProperty("items").EnumerateArray().First();
        card.GetProperty("completedCount").GetInt32().ShouldBe(0); // completion clawed back

        var feed = await fx.Get("/api/v1/notifications", auth);
        feed.GetProperty("items").EnumerateArray().Any(i => i.GetProperty("eventType").GetString() == "StampCardReversedV1").ShouldBeTrue();
    }

    [Fact]
    public async Task Promotions_report_aggregates_usage_rewards_and_liabilities()
    {
        var auth = await EarningApp("reports");
        await fx.Post("/api/v1/rewards", new { name = "Mug", pointsCost = 30, rewardType = "Discount", value = 1000 }, auth);

        // two members earn; one redeems + confirms
        await fx.PollRequest((await fx.Post("/api/v1/events", Purchase("P1", "ed", 50000), Sub(auth, "p1"))).GetProperty("requestId").GetString()!, auth);
        await fx.PollRequest((await fx.Post("/api/v1/events", Purchase("P2", "fay", 100000), Sub(auth, "p2"))).GetProperty("requestId").GetString()!, auth);
        var ed = await fx.Get("/api/v1/members/by-external-id/ed", auth);
        var reward = (await fx.Get("/api/v1/rewards?activeOnly=true", auth)).EnumerateArray().First().GetProperty("id").GetString()!;
        var red = await fx.Post($"/api/v1/members/{ed.GetProperty("memberId").GetString()}/redemptions", new { rewardId = reward }, auth);
        await fx.Post($"/api/v1/redemptions/{red.GetProperty("id").GetString()}/confirm", new { }, auth);

        var rep = await fx.Get("/api/v1/analytics/promotions", auth);

        // campaign shows executions + points
        var baseEarn = rep.GetProperty("campaigns").EnumerateArray().First(c => c.GetProperty("name").GetString() == "Base Earn");
        baseEarn.GetProperty("executions").GetInt32().ShouldBe(2);
        baseEarn.GetProperty("members").GetInt32().ShouldBe(2);
        baseEarn.GetProperty("pointsIssued").GetInt64().ShouldBe(150); // 50 + 100

        // reward redemption funnel: 1 confirmed → rate 1.0
        var mug = rep.GetProperty("rewards").EnumerateArray().First(r => r.GetProperty("name").GetString() == "Mug");
        mug.GetProperty("confirmed").GetInt32().ShouldBe(1);
        mug.GetProperty("redemptionRate").GetDouble().ShouldBe(1.0);

        // AOV = 150,000 / 2 purchases ; outstanding points = 150 earned − 30 redeemed = 120
        rep.GetProperty("averageOrderValue").GetDecimal().ShouldBe(75000);
        rep.GetProperty("liabilities").GetProperty("outstandingPoints").GetInt64().ShouldBe(120);
    }
}
