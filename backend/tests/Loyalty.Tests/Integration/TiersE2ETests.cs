using Shouldly;

namespace Loyalty.Tests.Integration;

/// <summary>
/// Phase-7 tiers: a member crosses a tier threshold and the new tier's earn-multiplier is applied
/// to the same purchase; the value profile reflects the new tier.
/// </summary>
[Collection("api")]
public class TiersE2ETests(ApiFixture fx)
{
    private static Dictionary<string, string> Sub(Dictionary<string, string> auth, string k) =>
        new() { ["X-Api-Key"] = auth["X-Api-Key"], ["Idempotency-Key"] = k };

    private static object Purchase(string orderId, int amount) => new
    {
        eventId = Guid.NewGuid(), eventType = "PurchaseCompleted", eventVersion = 1,
        sourceTransactionId = orderId, customerId = "tier-cust", occurredAt = DateTimeOffset.UtcNow,
        correlationId = orderId, attributes = new { orderId, amount, currency = "IQD" },
    };

    [Fact]
    public async Task Crossing_a_threshold_upgrades_tier_and_applies_multiplier()
    {
        var (appId, apiKey) = await fx.OnboardChildAsync("tiers");
        var auth = ApiFixture.Auth(apiKey);

        await fx.Put($"/api/v1/applications/{appId}/loyalty-settings", new
        {
            pointsUnitCode = "POINT", baseCurrency = "IQD", spendAmountPerPoint = 1000,
            roundingPolicy = "Floor", loyaltyStatus = "Active",
        }, auth);

        // Bronze (base, ×1.0) and Silver (netSpend >= 100000, ×1.5)
        await fx.Post("/api/v1/tiers", new { name = "Bronze", rank = 0, thresholdMetric = "NetSpend", thresholdValue = 0, earnMultiplier = 1.0, color = "#cd7f32" }, auth);
        await fx.Post("/api/v1/tiers", new { name = "Silver", nameAr = "فضي", rank = 1, thresholdMetric = "NetSpend", thresholdValue = 100000, earnMultiplier = 1.5, color = "#c0c0c0" }, auth);

        var c = await fx.Post("/api/v1/campaigns", new
        {
            name = "Base Earn", triggerEventType = "PurchaseCompleted", minimumEventVersion = 1,
            priority = 100, stackingPolicy = "Stackable", conditions = Array.Empty<object>(),
            effects = new[] { new { effectId = "base-earn", type = "AwardBasePoints" } },
        }, auth);
        await fx.ActivateCampaign(c.GetProperty("id").GetString()!, auth);

        // purchase 1: 50,000 IQD → Bronze (×1.0) → 50 points
        var r1 = await fx.Post("/api/v1/events", Purchase("T1", 50000), Sub(auth, "t1"));
        (await fx.PollRequest(r1.GetProperty("requestId").GetString()!, auth)).GetProperty("awardedPoints").GetInt64().ShouldBe(50);

        var m = await fx.Get("/api/v1/members/by-external-id/tier-cust", auth);
        var memberId = m.GetProperty("memberId").GetString()!;
        (await fx.Get($"/api/v1/members/{memberId}/value-profile", auth))
            .GetProperty("tier").GetProperty("name").GetString().ShouldBe("Bronze");

        // purchase 2: +100,000 IQD → netSpend 150,000 crosses Silver → base 100 × 1.5 = 150 points
        var r2 = await fx.Post("/api/v1/events", Purchase("T2", 100000), Sub(auth, "t2"));
        (await fx.PollRequest(r2.GetProperty("requestId").GetString()!, auth)).GetProperty("awardedPoints").GetInt64().ShouldBe(150);

        var profile = await fx.Get($"/api/v1/members/{memberId}/value-profile", auth);
        profile.GetProperty("tier").GetProperty("name").GetString().ShouldBe("Silver");
        (await fx.Get($"/api/v1/members/{memberId}/wallet", auth)).GetProperty("activeBalance").GetInt64().ShouldBe(200);

        // a MemberTierChangedV1 event was emitted
        var feed = await fx.Get("/api/v1/notifications", auth);
        feed.GetProperty("items").EnumerateArray()
            .Any(i => i.GetProperty("eventType").GetString() == "MemberTierChangedV1").ShouldBeTrue();
    }
}
