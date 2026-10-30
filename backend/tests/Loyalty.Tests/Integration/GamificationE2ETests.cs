using Shouldly;

namespace Loyalty.Tests.Integration;

/// <summary>
/// Phase-9 gamification: a purchase unlocks a metric-based achievement (bonus points + streak),
/// the leaderboard ranks the member, and a time-boxed challenge completes after enough events.
/// </summary>
[Collection("api")]
public class GamificationE2ETests(ApiFixture fx)
{
    private static Dictionary<string, string> Sub(Dictionary<string, string> auth, string k) =>
        new() { ["X-Api-Key"] = auth["X-Api-Key"], ["Idempotency-Key"] = k };

    private static object Purchase(string orderId, string customer, int amount) => new
    {
        eventId = Guid.NewGuid(), eventType = "PurchaseCompleted", eventVersion = 1,
        sourceTransactionId = orderId, customerId = customer, occurredAt = DateTimeOffset.UtcNow,
        correlationId = orderId, attributes = new { orderId, amount, currency = "IQD" },
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
    public async Task Purchase_unlocks_achievement_with_bonus_and_ranks_on_leaderboard()
    {
        var auth = await EarningApp("gam");

        // achievement: first purchase → +50 bonus
        await fx.Post("/api/v1/achievements", new
        {
            name = "First Purchase", nameAr = "أول شراء", metric = "PurchaseCount", threshold = 1, bonusPoints = 50,
        }, auth);

        // 10,000 IQD → base 10; achievement unlock → +50 => 60 total
        var submit = await fx.Post("/api/v1/events", Purchase("G1", "player", 10000), Sub(auth, "g1"));
        var final = await fx.PollRequest(submit.GetProperty("requestId").GetString()!, auth);
        final.GetProperty("status").GetString().ShouldBe("Completed");
        final.GetProperty("awardedPoints").GetInt64().ShouldBe(60);

        var m = await fx.Get("/api/v1/members/by-external-id/player", auth);
        var memberId = m.GetProperty("memberId").GetString()!;
        var profile = await fx.Get($"/api/v1/members/{memberId}/value-profile", auth);
        profile.GetProperty("pointsEarned").GetInt64().ShouldBe(60);
        profile.GetProperty("currentStreak").GetInt32().ShouldBe(1);

        var badges = await fx.Get($"/api/v1/members/{memberId}/achievements", auth);
        badges.GetProperty("items").EnumerateArray().Any(a => a.GetProperty("name").GetString() == "First Purchase").ShouldBeTrue();

        var board = await fx.Get("/api/v1/leaderboard?metric=PointsEarned&limit=5", auth);
        var top = board.GetProperty("items").EnumerateArray().First();
        top.GetProperty("externalCustomerId").GetString().ShouldBe("player");
        top.GetProperty("value").GetDecimal().ShouldBe(60);

        // AchievementUnlockedV1 event emitted
        var feed = await fx.Get("/api/v1/notifications", auth);
        feed.GetProperty("items").EnumerateArray().Any(i => i.GetProperty("eventType").GetString() == "AchievementUnlockedV1").ShouldBeTrue();
    }

    [Fact]
    public async Task Challenge_completes_after_enough_events_and_awards_bonus()
    {
        var auth = await EarningApp("gam-challenge");

        // challenge: 2 purchases this window → +100
        await fx.Post("/api/v1/challenges", new
        {
            name = "Two Buys", metric = "PurchaseCount", targetValue = 2,
            startsAtUtc = DateTimeOffset.UtcNow.AddHours(-1), endsAtUtc = DateTimeOffset.UtcNow.AddDays(1), bonusPoints = 100,
        }, auth);

        var r1 = await fx.Post("/api/v1/events", Purchase("C1", "challenger", 1000), Sub(auth, "c1"));
        (await fx.PollRequest(r1.GetProperty("requestId").GetString()!, auth)).GetProperty("awardedPoints").GetInt64().ShouldBe(1); // base only

        var r2 = await fx.Post("/api/v1/events", Purchase("C2", "challenger", 1000), Sub(auth, "c2"));
        (await fx.PollRequest(r2.GetProperty("requestId").GetString()!, auth)).GetProperty("awardedPoints").GetInt64().ShouldBe(101); // base 1 + challenge 100

        var m = await fx.Get("/api/v1/members/by-external-id/challenger", auth);
        (await fx.Get($"/api/v1/members/{m.GetProperty("memberId").GetString()}/value-profile", auth))
            .GetProperty("pointsEarned").GetInt64().ShouldBe(102);
    }
}
