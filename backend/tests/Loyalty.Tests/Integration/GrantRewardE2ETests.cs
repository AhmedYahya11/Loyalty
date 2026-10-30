using Shouldly;

namespace Loyalty.Tests.Integration;

/// <summary>
/// Phase-16: a campaign GrantReward effect issues a free coupon directly on a qualifying event
/// (spend ≥ value, or a CustomAction like app.shared), idempotently and respecting the reward's
/// per-member limit; the granted (0-cost) coupon can be confirmed without touching the ledger.
/// </summary>
[Collection("api")]
public class GrantRewardE2ETests(ApiFixture fx)
{
    private static Dictionary<string, string> Sub(Dictionary<string, string> auth, string k) =>
        new() { ["X-Api-Key"] = auth["X-Api-Key"], ["Idempotency-Key"] = k };

    private static object Purchase(string orderId, string customer, int amount) => new
    {
        eventId = Guid.NewGuid(), eventType = "PurchaseCompleted", eventVersion = 1,
        sourceTransactionId = orderId, customerId = customer, occurredAt = DateTimeOffset.UtcNow,
        correlationId = orderId, attributes = new { orderId, amount, currency = "IQD" },
    };

    private async Task<(string appId, Dictionary<string, string> auth)> App(string name)
    {
        var (appId, apiKey) = await fx.OnboardChildAsync(name);
        var auth = ApiFixture.Auth(apiKey);
        await fx.Put($"/api/v1/applications/{appId}/loyalty-settings", new
        {
            pointsUnitCode = "POINT", baseCurrency = "IQD", spendAmountPerPoint = 1000, roundingPolicy = "Floor", loyaltyStatus = "Active",
        }, auth);
        return (appId, auth);
    }

    [Fact]
    public async Task Spend_threshold_campaign_grants_a_free_coupon_idempotently()
    {
        var (_, auth) = await App("grant");
        var reward = await fx.Post("/api/v1/rewards", new { name = "Free dessert", nameAr = "حلوى مجانية", pointsCost = 1, rewardType = "FreeItem", value = 5000, perMemberLimit = 1 }, auth);
        var rewardId = reward.GetProperty("id").GetString()!;

        // campaign: spend >= 100,000 → grant the free-dessert coupon (no points)
        var c = await fx.Post("/api/v1/campaigns", new
        {
            name = "Big spender gift", triggerEventType = "PurchaseCompleted", minimumEventVersion = 1, priority = 100,
            stackingPolicy = "Stackable",
            conditions = new[] { new { field = "amount", @operator = "GreaterThanOrEqual", value = "100000" } },
            effects = new[] { new { effectId = "gift", type = "GrantReward", rewardId } },
        }, auth);
        await fx.ActivateCampaign(c.GetProperty("id").GetString()!, auth);

        // a small purchase does NOT qualify; a big one grants the coupon
        await fx.PollRequest((await fx.Post("/api/v1/events", Purchase("S1", "gwen", 10000), Sub(auth, "s1"))).GetProperty("requestId").GetString()!, auth);
        (await fx.PollRequest((await fx.Post("/api/v1/events", Purchase("B1", "gwen", 150000), Sub(auth, "b1"))).GetProperty("requestId").GetString()!, auth))
            .GetProperty("status").GetString().ShouldBe("Completed");

        var m = await fx.Get("/api/v1/members/by-external-id/gwen", auth);
        var memberId = m.GetProperty("memberId").GetString()!;
        var reds = (await fx.Get($"/api/v1/members/{memberId}/redemptions", auth)).GetProperty("items").EnumerateArray().ToList();
        reds.Count.ShouldBe(1); // exactly one coupon granted
        var granted = reds[0];
        granted.GetProperty("rewardName").GetString().ShouldBe("Free dessert");
        granted.GetProperty("pointsCost").GetInt64().ShouldBe(0); // free
        granted.GetProperty("status").GetString().ShouldBe("Reserved");
        granted.GetProperty("couponCode").GetString().ShouldNotBeNullOrWhiteSpace();

        // a second big purchase does NOT grant again (reward per-member limit = 1)
        await fx.PollRequest((await fx.Post("/api/v1/events", Purchase("B2", "gwen", 200000), Sub(auth, "b2"))).GetProperty("requestId").GetString()!, auth);
        (await fx.Get($"/api/v1/members/{memberId}/redemptions", auth)).GetProperty("items").EnumerateArray().Count().ShouldBe(1);

        // the free coupon confirms without any ledger/points movement
        var confirm = await fx.Post($"/api/v1/redemptions/{granted.GetProperty("id").GetString()}/confirm", new { }, auth);
        confirm.GetProperty("status").GetString().ShouldBe("Confirmed");
        (await fx.Get($"/api/v1/members/{memberId}/value-profile", auth)).GetProperty("pointsRedeemed").GetInt64().ShouldBe(0);

        var feed = await fx.Get("/api/v1/notifications", auth);
        feed.GetProperty("items").EnumerateArray().Any(i => i.GetProperty("eventType").GetString() == "RewardGrantedV1").ShouldBeTrue();
    }

    [Fact]
    public async Task Sharing_the_app_grants_a_voucher_via_custom_action()
    {
        var (_, auth) = await App("grant-share");
        var reward = await fx.Post("/api/v1/rewards", new { name = "Share voucher", pointsCost = 1, rewardType = "Voucher", value = 2000 }, auth);
        var rewardId = reward.GetProperty("id").GetString()!;
        var c = await fx.Post("/api/v1/campaigns", new
        {
            name = "Share & get voucher", triggerEventType = "CustomAction", minimumEventVersion = 1, priority = 100, stackingPolicy = "Stackable",
            conditions = new[] { new { field = "attributes.action", @operator = "Equals", value = "app.shared" } },
            effects = new[] { new { effectId = "voucher", type = "GrantReward", rewardId } },
        }, auth);
        await fx.ActivateCampaign(c.GetProperty("id").GetString()!, auth);

        await fx.PollRequest((await fx.Post("/api/v1/events", new { eventId = Guid.NewGuid(), eventType = "CustomAction", eventVersion = 1, sourceTransactionId = "sh1", customerId = "hira", occurredAt = DateTimeOffset.UtcNow, correlationId = "sh1", attributes = new { action = "app.shared", actionId = "sh1" } }, Sub(auth, "shr1"))).GetProperty("requestId").GetString()!, auth);

        var m = await fx.Get("/api/v1/members/by-external-id/hira", auth);
        var reds = (await fx.Get($"/api/v1/members/{m.GetProperty("memberId").GetString()}/redemptions", auth)).GetProperty("items").EnumerateArray().ToList();
        reds.Count.ShouldBe(1);
        reds[0].GetProperty("rewardName").GetString().ShouldBe("Share voucher");
    }
}
