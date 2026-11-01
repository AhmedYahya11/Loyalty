using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace Loyalty.Tests.Integration;

/// <summary>
/// B3 — per-member earn-rate limiting (anti point-farming). With a tenant opt-in cap
/// (<c>MaxEarnEventsPerHour</c>), a single member can only submit that many earning events per rolling
/// hour; the next one is rejected with 429, while other members and the default (unlimited) tenants are
/// unaffected.
/// </summary>
[Collection("api")]
public class MemberEarnRateLimitE2ETests(ApiFixture fx)
{
    private static Dictionary<string, string> Sub(string apiKey, string k) =>
        new() { ["X-Api-Key"] = apiKey, ["Idempotency-Key"] = k };

    private static object Purchase(string orderId, string customerId) => new
    {
        eventId = Guid.NewGuid(), eventType = "PurchaseCompleted", eventVersion = 1,
        sourceTransactionId = orderId, customerId, occurredAt = DateTimeOffset.UtcNow,
        correlationId = orderId, attributes = new { orderId, amount = 20000, currency = "IQD" },
    };

    private async Task<HttpResponseMessage> Submit(string apiKey, object body, string key)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/events") { Content = JsonContent.Create(body) };
        req.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
        req.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return await fx.Client.SendAsync(req);
    }

    [Fact]
    public async Task Per_member_earn_rate_limit_rejects_the_overflow_event()
    {
        var (appId, apiKey) = await fx.OnboardChildAsync("earn-rate");

        // opt-in: at most 2 earning events per member per hour
        await fx.Put($"/api/v1/applications/{appId}/loyalty-settings", new
        {
            pointsUnitCode = "POINT", baseCurrency = "IQD", spendAmountPerPoint = 1000,
            roundingPolicy = "Floor", loyaltyStatus = "Active", maxEarnEventsPerHour = 2,
        }, Auth(apiKey));

        // the same member's first two earning events are accepted
        (await Submit(apiKey, Purchase("F-1", "farmer"), "f1")).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await Submit(apiKey, Purchase("F-2", "farmer"), "f2")).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // the third within the hour is rate-limited
        var third = await Submit(apiKey, Purchase("F-3", "farmer"), "f3");
        third.StatusCode.ShouldBe((HttpStatusCode)429);
        (await third.Content.ReadAsStringAsync()).ShouldContain("earn_rate_limited");

        // a different member is unaffected (the cap is per-member)
        (await Submit(apiKey, Purchase("O-1", "other-member"), "o1")).StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Default_tenant_has_no_earn_rate_limit()
    {
        var (appId, apiKey) = await fx.OnboardChildAsync("earn-rate-off");
        await fx.Put($"/api/v1/applications/{appId}/loyalty-settings", new
        {
            pointsUnitCode = "POINT", baseCurrency = "IQD", spendAmountPerPoint = 1000,
            roundingPolicy = "Floor", loyaltyStatus = "Active", // MaxEarnEventsPerHour defaults to 0 (off)
        }, Auth(apiKey));

        for (var i = 0; i < 5; i++)
            (await Submit(apiKey, Purchase($"N-{i}", "heavy-user"), $"n{i}")).StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    private static Dictionary<string, string> Auth(string key) => ApiFixture.Auth(key);
}
