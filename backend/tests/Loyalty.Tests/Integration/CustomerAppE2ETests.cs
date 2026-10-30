using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace Loyalty.Tests.Integration;

/// <summary>
/// Phase-11 customer-facing app: an admin mints a member access token; the token authenticates the
/// customer surface scoped to that one member (their points, rewards, redemptions) — and a customer
/// can neither use an invalid token nor act on another member's data.
/// </summary>
[Collection("api")]
public class CustomerAppE2ETests(ApiFixture fx)
{
    private static Dictionary<string, string> Bearer(string token) => new() { ["Authorization"] = $"Bearer {token}" };
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
    public async Task Member_token_scopes_the_customer_surface_to_its_own_member()
    {
        var auth = await EarningApp("cust-app");
        await fx.Post("/api/v1/rewards", new { name = "Free coffee", nameAr = "قهوة مجانية", pointsCost = 10, rewardType = "Discount", value = 1000 }, auth);

        // owner earns points; a token is minted for them
        var owner = await fx.Post("/api/v1/members", new { externalCustomerId = "owner" }, auth);
        var ownerId = owner.GetProperty("memberId").GetString()!;
        var sub = await fx.Post("/api/v1/events", Purchase("O1", "owner", 50000), Sub(auth, "o1"));
        (await fx.PollRequest(sub.GetProperty("requestId").GetString()!, auth)).GetProperty("status").GetString().ShouldBe("Completed");

        var token = (await fx.Post($"/api/v1/members/{ownerId}/access-token", new { }, auth)).GetProperty("token").GetString()!;
        token.ShouldStartWith("mt_");

        // /customer/me returns the owner's own balance (50 points from 50,000 IQD)
        var me = await fx.Get("/api/v1/customer/me", Bearer(token));
        me.GetProperty("member").GetProperty("externalCustomerId").GetString().ShouldBe("owner");
        me.GetProperty("member").GetProperty("wallet").GetProperty("activeBalance").GetInt64().ShouldBe(50);

        // rewards show an affordability flag; owner can afford the 10-pt coffee
        var rewards = await fx.Get("/api/v1/customer/rewards", Bearer(token));
        var coffee = rewards.EnumerateArray().First();
        coffee.GetProperty("affordable").GetBoolean().ShouldBeTrue();

        // owner reserves it via the customer surface → appears in their coupons
        var redeemed = await fx.Post("/api/v1/customer/redemptions", new { rewardId = coffee.GetProperty("id").GetString() }, Bearer(token));
        redeemed.GetProperty("status").GetString().ShouldBe("Reserved");
        var reds = await fx.Get("/api/v1/customer/redemptions", Bearer(token));
        reds.GetProperty("items").EnumerateArray().Count().ShouldBe(1);

        // active balance moved to reserved (50 - 10)
        (await fx.Get("/api/v1/customer/me", Bearer(token))).GetProperty("member").GetProperty("wallet").GetProperty("activeBalance").GetInt64().ShouldBe(40);
    }

    [Fact]
    public async Task Invalid_token_is_rejected_and_a_customer_cannot_touch_another_member()
    {
        var auth = await EarningApp("cust-app-sec");
        await fx.Post("/api/v1/rewards", new { name = "Gift", pointsCost = 5, rewardType = "Discount", value = 500 }, auth);

        var owner = await fx.Post("/api/v1/members", new { externalCustomerId = "sec-owner" }, auth);
        var ownerId = owner.GetProperty("memberId").GetString()!;
        var token = (await fx.Post($"/api/v1/members/{ownerId}/access-token", new { }, auth)).GetProperty("token").GetString()!;

        // another member reserves a redemption (via admin)
        var other = await fx.Post("/api/v1/members", new { externalCustomerId = "sec-other" }, auth);
        var otherId = other.GetProperty("memberId").GetString()!;
        await fx.Post("/api/v1/events", Purchase("X1", "sec-other", 50000), Sub(auth, "x1"));
        var reward = (await fx.Get("/api/v1/rewards?activeOnly=true", auth)).EnumerateArray().First().GetProperty("id").GetString()!;
        // wait for sec-other to have points
        for (var i = 0; i < 20 && (await fx.Get($"/api/v1/members/{otherId}/wallet", auth)).GetProperty("activeBalance").GetInt64() == 0; i++)
            await Task.Delay(300);
        var otherRedemption = await fx.Post($"/api/v1/members/{otherId}/redemptions", new { rewardId = reward }, auth);
        var otherRedemptionId = otherRedemption.GetProperty("id").GetString()!;

        // invalid token → 401
        var bad = new HttpRequestMessage(HttpMethod.Get, "/api/v1/customer/me");
        bad.Headers.TryAddWithoutValidation("Authorization", "Bearer mt_deadbeef");
        (await fx.Client.SendAsync(bad)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // owner's token cannot cancel sec-other's redemption → 404 (not visible as "owned")
        var attack = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/customer/redemptions/{otherRedemptionId}/cancel")
        { Content = JsonContent.Create(new { }) };
        attack.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        (await fx.Client.SendAsync(attack)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // and sec-other's redemption is still intact (owner couldn't touch it)
        (await fx.Get($"/api/v1/redemptions/by-code/{otherRedemption.GetProperty("couponCode").GetString()}", auth))
            .GetProperty("status").GetString().ShouldBe("Reserved");
    }
}
