using Shouldly;

namespace Loyalty.Tests.Integration;

/// <summary>
/// Phase-13: points expire FIFO after the configured window (recent points + redeemed points are
/// protected), and a stamp card only earns a stamp for events that match its attribute conditions
/// and fall inside its validity window.
/// </summary>
[Collection("api")]
public class ExpiryAndStampConditionsE2ETests(ApiFixture fx)
{
    private static Dictionary<string, string> Sub(Dictionary<string, string> auth, string k) =>
        new() { ["X-Api-Key"] = auth["X-Api-Key"], ["Idempotency-Key"] = k };

    private static object Purchase(string orderId, string customer, int amount, int daysAgo = 0, object? extra = null) => new
    {
        eventId = Guid.NewGuid(), eventType = "PurchaseCompleted", eventVersion = 1,
        sourceTransactionId = orderId, customerId = customer, occurredAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
        correlationId = orderId,
        attributes = extra is null
            ? new Dictionary<string, object> { ["orderId"] = orderId, ["amount"] = amount, ["currency"] = "IQD" }
            : Merge(new Dictionary<string, object> { ["orderId"] = orderId, ["amount"] = amount, ["currency"] = "IQD" }, extra),
    };

    private static Dictionary<string, object> Merge(Dictionary<string, object> a, object b)
    {
        foreach (var pr in b.GetType().GetProperties()) a[pr.Name] = pr.GetValue(b)!;
        return a;
    }

    private async Task<(string appId, Dictionary<string, string> auth)> EarningApp(string name, int expiryDays)
    {
        var (appId, apiKey) = await fx.OnboardChildAsync(name);
        var auth = ApiFixture.Auth(apiKey);
        await fx.Put($"/api/v1/applications/{appId}/loyalty-settings", new
        {
            pointsUnitCode = "POINT", baseCurrency = "IQD", spendAmountPerPoint = 1000, roundingPolicy = "Floor",
            loyaltyStatus = "Active", pointsExpiryDays = expiryDays,
        }, auth);
        var c = await fx.Post("/api/v1/campaigns", new
        {
            name = "Base Earn", triggerEventType = "PurchaseCompleted", minimumEventVersion = 1,
            priority = 100, stackingPolicy = "Stackable", conditions = Array.Empty<object>(),
            effects = new[] { new { effectId = "base-earn", type = "AwardBasePoints" } },
        }, auth);
        await fx.ActivateCampaign(c.GetProperty("id").GetString()!, auth);
        return (appId, auth);
    }

    [Fact]
    public async Task Stale_points_expire_fifo_while_recent_points_are_kept()
    {
        var (_, auth) = await EarningApp("expiry", expiryDays: 30);

        // old earn (60 days ago → 100 pts) + recent earn (today → 20 pts)
        await fx.PollRequest((await fx.Post("/api/v1/events", Purchase("E-old", "ann", 100000, daysAgo: 60), Sub(auth, "e-old"))).GetProperty("requestId").GetString()!, auth);
        await fx.PollRequest((await fx.Post("/api/v1/events", Purchase("E-new", "ann", 20000, daysAgo: 0), Sub(auth, "e-new"))).GetProperty("requestId").GetString()!, auth);

        var m = await fx.Get("/api/v1/members/by-external-id/ann", auth);
        var id = m.GetProperty("memberId").GetString()!;
        (await fx.Get($"/api/v1/members/{id}/wallet", auth)).GetProperty("activeBalance").GetInt64().ShouldBe(120);

        // expire → only the 100 old points go; the 20 recent ones remain
        (await fx.Post("/api/v1/admin/expire-points", new { }, auth)).GetProperty("expired").GetInt64().ShouldBeGreaterThanOrEqualTo(100);
        (await fx.Get($"/api/v1/members/{id}/wallet", auth)).GetProperty("activeBalance").GetInt64().ShouldBe(20);

        // re-running is idempotent (recent points not yet stale)
        (await fx.Post("/api/v1/admin/expire-points", new { }, auth)).GetProperty("expired").GetInt64().ShouldBe(0);
        (await fx.Get($"/api/v1/members/{id}/wallet", auth)).GetProperty("activeBalance").GetInt64().ShouldBe(20);

        var profile = await fx.Get($"/api/v1/members/{id}/value-profile", auth);
        profile.GetProperty("pointsExpired").GetInt64().ShouldBe(100);
    }

    [Fact]
    public async Task Redeemed_old_points_are_not_double_counted_by_expiry()
    {
        var (_, auth) = await EarningApp("expiry-redeem", expiryDays: 30);
        await fx.Post("/api/v1/rewards", new { name = "Treat", pointsCost = 40, rewardType = "Discount", value = 1000 }, auth);

        // 100 old points, then redeem 40 → 60 active; only 60 should expire
        await fx.PollRequest((await fx.Post("/api/v1/events", Purchase("R-old", "bob", 100000, daysAgo: 60), Sub(auth, "r1"))).GetProperty("requestId").GetString()!, auth);
        var m = await fx.Get("/api/v1/members/by-external-id/bob", auth);
        var id = m.GetProperty("memberId").GetString()!;
        var reward = (await fx.Get("/api/v1/rewards?activeOnly=true", auth)).EnumerateArray().First().GetProperty("id").GetString()!;
        var red = await fx.Post($"/api/v1/members/{id}/redemptions", new { rewardId = reward }, auth);
        await fx.Post($"/api/v1/redemptions/{red.GetProperty("id").GetString()}/confirm", new { }, auth); // 40 leaves the wallet

        (await fx.Get($"/api/v1/members/{id}/wallet", auth)).GetProperty("activeBalance").GetInt64().ShouldBe(60);
        (await fx.Post("/api/v1/admin/expire-points", new { }, auth)).GetProperty("expired").GetInt64().ShouldBe(60);
        (await fx.Get($"/api/v1/members/{id}/wallet", auth)).GetProperty("activeBalance").GetInt64().ShouldBe(0);
    }

    [Fact]
    public async Task Stamp_card_only_counts_matching_service_within_validity()
    {
        var (_, auth) = await EarningApp("stamp-cond", expiryDays: 0);
        // card: 2 LASER sessions → +500, valid this year only
        await fx.Post("/api/v1/stamp-cards", new
        {
            name = "Laser loyalty", stampsRequired = 2, rewardPoints = 500, triggerEventType = "PurchaseCompleted",
            conditions = new[] { new { field = "attributes.service", @operator = "Equals", value = "laser" } },
            startsAtUtc = DateTimeOffset.UtcNow.AddDays(-1), endsAtUtc = DateTimeOffset.UtcNow.AddDays(30),
        }, auth);

        // a facial does NOT earn a stamp; two laser sessions do → card completes (+500)
        await fx.PollRequest((await fx.Post("/api/v1/events", Purchase("F1", "cara", 1000, extra: new { service = "facial" }), Sub(auth, "f1"))).GetProperty("requestId").GetString()!, auth);
        await fx.PollRequest((await fx.Post("/api/v1/events", Purchase("L1", "cara", 1000, extra: new { service = "laser" }), Sub(auth, "l1"))).GetProperty("requestId").GetString()!, auth);
        var completing = await fx.PollRequest((await fx.Post("/api/v1/events", Purchase("L2", "cara", 1000, extra: new { service = "laser" }), Sub(auth, "l2"))).GetProperty("requestId").GetString()!, auth);
        completing.GetProperty("awardedPoints").GetInt64().ShouldBe(501); // base 1 + stamp 500

        var m = await fx.Get("/api/v1/members/by-external-id/cara", auth);
        var progress = await fx.Get($"/api/v1/members/{m.GetProperty("memberId").GetString()}/stamp-cards", auth);
        var card = progress.GetProperty("items").EnumerateArray().First();
        card.GetProperty("completedCount").GetInt32().ShouldBe(1); // only the 2 laser visits counted
        card.GetProperty("stamps").GetInt32().ShouldBe(0);
    }
}
