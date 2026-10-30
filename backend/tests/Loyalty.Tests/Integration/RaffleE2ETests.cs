using Shouldly;

namespace Loyalty.Tests.Integration;

/// <summary>
/// Phase-15: qualifying purchases (≥ a threshold) earn weighted raffle entries, a generic
/// CustomAction (e.g. app.shared) earns an entry too, and a draw picks a weighted-random winner,
/// awards the prize points, and is idempotent.
/// </summary>
[Collection("api")]
public class RaffleE2ETests(ApiFixture fx)
{
    private static Dictionary<string, string> Sub(Dictionary<string, string> auth, string k) =>
        new() { ["X-Api-Key"] = auth["X-Api-Key"], ["Idempotency-Key"] = k };

    private static object Purchase(string orderId, string customer, int amount) => new
    {
        eventId = Guid.NewGuid(), eventType = "PurchaseCompleted", eventVersion = 1,
        sourceTransactionId = orderId, customerId = customer, occurredAt = DateTimeOffset.UtcNow,
        correlationId = orderId, attributes = new { orderId, amount, currency = "IQD" },
    };

    private static object Shared(string actionId, string customer) => new
    {
        eventId = Guid.NewGuid(), eventType = "CustomAction", eventVersion = 1,
        sourceTransactionId = actionId, customerId = customer, occurredAt = DateTimeOffset.UtcNow,
        correlationId = actionId, attributes = new { action = "app.shared", actionId },
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
    public async Task Purchases_and_custom_actions_earn_entries_and_a_draw_picks_a_weighted_winner()
    {
        var auth = await EarningApp("raffle");
        // raffle: each purchase >= 50,000 → 1 entry; prize 5,000 points
        var r = await fx.Post("/api/v1/raffles", new
        {
            name = "Big draw", triggerEventType = "PurchaseCompleted", minAmount = 50000, entriesPerEvent = 1,
            prizePoints = 5000, prizeDescription = "iPhone", conditions = Array.Empty<object>(),
        }, auth);
        var raffleId = r.GetProperty("id").GetString()!;

        // whale: two big purchases → 2 entries; minnow: one small purchase → 0 entries (below threshold)
        await fx.PollRequest((await fx.Post("/api/v1/events", Purchase("W1", "whale", 60000), Sub(auth, "w1"))).GetProperty("requestId").GetString()!, auth);
        await fx.PollRequest((await fx.Post("/api/v1/events", Purchase("W2", "whale", 60000), Sub(auth, "w2"))).GetProperty("requestId").GetString()!, auth);
        await fx.PollRequest((await fx.Post("/api/v1/events", Purchase("S1", "minnow", 1000), Sub(auth, "s1"))).GetProperty("requestId").GetString()!, auth);

        var entries = await fx.Get($"/api/v1/raffles/{raffleId}/entries", auth);
        var rows = entries.GetProperty("items").EnumerateArray().ToList();
        rows.First(x => x.GetProperty("externalCustomerId").GetString() == "whale").GetProperty("entries").GetInt64().ShouldBe(2);
        rows.Any(x => x.GetProperty("externalCustomerId").GetString() == "minnow").ShouldBeFalse(); // below min amount

        // the only eligible participant wins; prize points are awarded
        var drawn = await fx.Post($"/api/v1/raffles/{raffleId}/draw", new { }, auth);
        drawn.GetProperty("status").GetString().ShouldBe("Drawn");
        var winnerId = drawn.GetProperty("winnerMemberId").GetString()!;
        var whale = await fx.Get("/api/v1/members/by-external-id/whale", auth);
        winnerId.ShouldBe(whale.GetProperty("memberId").GetString());
        // 60 + 60 base earn + 5,000 raffle prize
        (await fx.Get($"/api/v1/members/{winnerId}/wallet", auth)).GetProperty("activeBalance").GetInt64().ShouldBe(5120);

        // drawing again is a no-op (same winner, no second prize)
        var again = await fx.Post($"/api/v1/raffles/{raffleId}/draw", new { }, auth);
        again.GetProperty("winnerMemberId").GetString().ShouldBe(winnerId);
        (await fx.Get($"/api/v1/members/{winnerId}/wallet", auth)).GetProperty("activeBalance").GetInt64().ShouldBe(5120);
    }

    [Fact]
    public async Task Sharing_the_app_earns_a_raffle_entry()
    {
        var auth = await EarningApp("raffle-share");
        var r = await fx.Post("/api/v1/raffles", new
        {
            name = "Share & win", triggerEventType = "CustomAction", minAmount = 0, entriesPerEvent = 1,
            prizePoints = 1000, conditions = new[] { new { field = "attributes.action", @operator = "Equals", value = "app.shared" } },
        }, auth);
        var raffleId = r.GetProperty("id").GetString()!;

        // a non-matching action earns nothing; a share earns an entry
        await fx.PollRequest((await fx.Post("/api/v1/events", new { eventId = Guid.NewGuid(), eventType = "CustomAction", eventVersion = 1, sourceTransactionId = "rv1", customerId = "gina", occurredAt = DateTimeOffset.UtcNow, correlationId = "rv1", attributes = new { action = "app.reviewed", actionId = "rv1" } }, Sub(auth, "rev1"))).GetProperty("requestId").GetString()!, auth);
        await fx.PollRequest((await fx.Post("/api/v1/events", Shared("sh1", "gina"), Sub(auth, "sh1"))).GetProperty("requestId").GetString()!, auth);

        var rows = (await fx.Get($"/api/v1/raffles/{raffleId}/entries", auth)).GetProperty("items").EnumerateArray().ToList();
        rows.Count.ShouldBe(1);
        rows[0].GetProperty("externalCustomerId").GetString().ShouldBe("gina");
        rows[0].GetProperty("entries").GetInt64().ShouldBe(1);
    }
}
