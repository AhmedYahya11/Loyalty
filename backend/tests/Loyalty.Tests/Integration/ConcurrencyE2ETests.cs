using Shouldly;

namespace Loyalty.Tests.Integration;

/// <summary>
/// Regression: multiple events for the SAME brand-new member, submitted concurrently, used to
/// race on member provisioning (unique-constraint violation → dead-letter). The handler now
/// retries that conflict (the loser re-runs, finds the member, completes). All events must
/// complete and the wallet must hold the full total — no lost purchases.
/// </summary>
[Collection("api")]
public class ConcurrencyE2ETests(ApiFixture fx)
{
    private static Dictionary<string, string> Sub(Dictionary<string, string> auth, string k) =>
        new() { ["X-Api-Key"] = auth["X-Api-Key"], ["Idempotency-Key"] = k };

    private static object Purchase(string orderId, string customerId, int amount) => new
    {
        eventId = Guid.NewGuid(), eventType = "PurchaseCompleted", eventVersion = 1,
        sourceTransactionId = orderId, customerId, occurredAt = DateTimeOffset.UtcNow,
        correlationId = orderId, attributes = new { orderId, amount, currency = "IQD" },
    };

    [Fact]
    public async Task Concurrent_events_for_a_new_member_all_process()
    {
        var (appId, apiKey) = await fx.OnboardChildAsync("concurrency");
        var auth = ApiFixture.Auth(apiKey);

        await fx.Put($"/api/v1/applications/{appId}/loyalty-settings", new
        {
            pointsUnitCode = "POINT", baseCurrency = "IQD", spendAmountPerPoint = 1000,
            roundingPolicy = "Floor", loyaltyStatus = "Active",
        }, auth);

        var c = await fx.Post("/api/v1/campaigns", new
        {
            name = "Base Earn", triggerEventType = "PurchaseCompleted", minimumEventVersion = 1,
            priority = 100, stackingPolicy = "Stackable", conditions = Array.Empty<object>(),
            effects = new[] { new { effectId = "base-earn", type = "AwardBasePoints" } },
        }, auth);
        await fx.ActivateCampaign(c.GetProperty("id").GetString()!, auth);

        // NO member created up front — the worker auto-provisions. Fire N events for the SAME
        // new customer at once so their handlers race on provisioning.
        const int n = 5;
        const string customer = "race-cust";
        var submits = await Task.WhenAll(Enumerable.Range(0, n).Select(i =>
            fx.Post("/api/v1/events", Purchase($"RACE-{i}", customer, 50000), Sub(auth, $"race-{i}"))));
        var requestIds = submits.Select(s => s.GetProperty("requestId").GetString()!).ToArray();

        // every event must reach Completed (none lost to a provisioning race)
        foreach (var id in requestIds)
            (await fx.PollRequest(id, auth)).GetProperty("status").GetString().ShouldBe("Completed");

        // the member exists once, with the full total (n × 50)
        var member = await fx.Get($"/api/v1/members/by-external-id/{customer}", auth);
        var memberId = member.GetProperty("memberId").GetString()!;
        (await fx.Get($"/api/v1/members/{memberId}/wallet", auth)).GetProperty("activeBalance").GetInt64().ShouldBe(n * 50);
        (await fx.Get($"/api/v1/members/{memberId}/value-profile", auth)).GetProperty("purchaseCount").GetInt64().ShouldBe(n);
    }
}
