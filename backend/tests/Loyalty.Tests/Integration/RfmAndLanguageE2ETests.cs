using System.Net;
using System.Text.Json;
using Shouldly;

namespace Loyalty.Tests.Integration;

/// <summary>
/// Phase-12: RFM recompute scores every member (1–5) and assigns a persona; the best R/F/M member
/// lands in a top persona and the distribution sums to the evaluated count. Also verifies the
/// trilingual path: an `Accept-Language: ku` error falls back to the Arabic message (shared script).
/// </summary>
[Collection("api")]
public class RfmAndLanguageE2ETests(ApiFixture fx)
{
    private static Dictionary<string, string> Sub(Dictionary<string, string> auth, string k) =>
        new() { ["X-Api-Key"] = auth["X-Api-Key"], ["Idempotency-Key"] = k };

    private static object Purchase(string orderId, string customer, int amount, int daysAgo = 0) => new
    {
        eventId = Guid.NewGuid(), eventType = "PurchaseCompleted", eventVersion = 1,
        sourceTransactionId = orderId, customerId = customer, occurredAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
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
    public async Task Rfm_recompute_scores_members_and_assigns_personas()
    {
        var auth = await EarningApp("rfm");

        // whale: three big recent purchases; mid: one medium; ghost: registered, never purchased
        foreach (var (n, amt) in new[] { ("w1", 100000), ("w2", 100000), ("w3", 100000) })
            await fx.PollRequest((await fx.Post("/api/v1/events", Purchase(n, "whale", amt), Sub(auth, n))).GetProperty("requestId").GetString()!, auth);
        await fx.PollRequest((await fx.Post("/api/v1/events", Purchase("m1", "mid", 5000, daysAgo: 90), Sub(auth, "m1"))).GetProperty("requestId").GetString()!, auth);
        await fx.Post("/api/v1/members", new { externalCustomerId = "ghost" }, auth);

        var recompute = await fx.Post("/api/v1/admin/rfm-recompute", new { }, auth);
        recompute.GetProperty("scored").GetInt32().ShouldBeGreaterThanOrEqualTo(3);

        // the whale is scored 5/5/5 → Champions
        var whale = await fx.Get("/api/v1/members/by-external-id/whale", auth);
        var whaleProfile = await fx.Get($"/api/v1/members/{whale.GetProperty("memberId").GetString()}/value-profile", auth);
        var rfm = whaleProfile.GetProperty("rfm");
        rfm.GetProperty("monetary").GetInt32().ShouldBe(5);
        rfm.GetProperty("frequency").GetInt32().ShouldBe(5);
        rfm.GetProperty("persona").GetString().ShouldBe("Champions");

        // every member has a persona and in-range scores
        var ghost = await fx.Get("/api/v1/members/by-external-id/ghost", auth);
        var ghostRfm = (await fx.Get($"/api/v1/members/{ghost.GetProperty("memberId").GetString()}/value-profile", auth)).GetProperty("rfm");
        ghostRfm.GetProperty("persona").GetString().ShouldNotBeNullOrEmpty();
        ghostRfm.GetProperty("monetary").GetInt32().ShouldBeInRange(1, 5);
        // whale spent more than the ghost → monetary score is at least as high
        ghostRfm.GetProperty("monetary").GetInt32().ShouldBeLessThanOrEqualTo(rfm.GetProperty("monetary").GetInt32());

        // analytics distribution sums to the evaluated count
        var summary = await fx.Get("/api/v1/analytics/rfm", auth);
        summary.GetProperty("evaluated").GetInt32().ShouldBe(3);
        summary.GetProperty("personas").EnumerateArray().Sum(p => p.GetProperty("count").GetInt32()).ShouldBe(3);
    }

    [Fact]
    public async Task Kurdish_accept_language_falls_back_to_arabic_error()
    {
        // an invalid member token with Accept-Language: ku → 401 with the Arabic message (ku→ar fallback)
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/customer/me");
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer mt_bogus");
        req.Headers.TryAddWithoutValidation("Accept-Language", "ku");
        var resp = await fx.Client.SendAsync(req);
        resp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var body = await resp.Content.ReadAsStringAsync();
        var message = JsonDocument.Parse(body).RootElement.GetProperty("message").GetString()!;
        message.ShouldBe("مفتاح الواجهة البرمجية مفقود أو غير صالح."); // Arabic fallback, not the English text
        message.ShouldNotBe("Missing or invalid API key.");
    }
}
