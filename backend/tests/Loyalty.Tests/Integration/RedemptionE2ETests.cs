using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace Loyalty.Tests.Integration;

/// <summary>
/// Phase-8 rewards & redemptions: a member earns points, reserves a reward (active→reserved +
/// coupon), confirms (reserved points spent, PointsRedeemed recorded), and a separate reservation
/// is cancelled (points released back to active).
/// </summary>
[Collection("api")]
public class RedemptionE2ETests(ApiFixture fx)
{
    private static Dictionary<string, string> Sub(Dictionary<string, string> auth, string k) =>
        new() { ["X-Api-Key"] = auth["X-Api-Key"], ["Idempotency-Key"] = k };

    [Fact]
    public async Task Earn_reserve_confirm_spends_points_and_cancel_releases()
    {
        var (appId, apiKey) = await fx.OnboardChildAsync("redeem");
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

        // earn 500 points (500,000 IQD)
        var evt = new
        {
            eventId = Guid.NewGuid(), eventType = "PurchaseCompleted", eventVersion = 1,
            sourceTransactionId = "RDM-ORDER", customerId = "redeemer", occurredAt = DateTimeOffset.UtcNow,
            correlationId = "rdm", attributes = new { orderId = "RDM-ORDER", amount = 500000, currency = "IQD" },
        };
        var submit = await fx.Post("/api/v1/events", evt, Sub(auth, "rdm-earn"));
        await fx.PollRequest(submit.GetProperty("requestId").GetString()!, auth);

        var member = await fx.Get("/api/v1/members/by-external-id/redeemer", auth);
        var memberId = member.GetProperty("memberId").GetString()!;
        (await fx.Get($"/api/v1/members/{memberId}/wallet", auth)).GetProperty("activeBalance").GetInt64().ShouldBe(500);

        // reward: Free Coffee, 100 points, limited stock
        var reward = await fx.Post("/api/v1/rewards", new
        {
            name = "Free Coffee", nameAr = "قهوة مجانية", pointsCost = 100, rewardType = "Coupon",
            value = 2500, inventory = 5, perMemberLimit = 3,
        }, auth);
        var rewardId = reward.GetProperty("id").GetString()!;

        // reserve → active 500→400, reserved 0→100, coupon issued
        var reserved = await fx.Post($"/api/v1/members/{memberId}/redemptions", new { rewardId }, auth);
        reserved.GetProperty("status").GetString().ShouldBe("Reserved");
        var couponCode = reserved.GetProperty("couponCode").GetString()!;
        var redemptionId = reserved.GetProperty("id").GetString()!;
        var w1 = await fx.Get($"/api/v1/members/{memberId}/wallet", auth);
        w1.GetProperty("activeBalance").GetInt64().ShouldBe(400);
        w1.GetProperty("reservedBalance").GetInt64().ShouldBe(100);

        // the coupon is lookupable (POS validation)
        (await fx.Get($"/api/v1/redemptions/by-code/{couponCode}", auth)).GetProperty("status").GetString().ShouldBe("Reserved");

        // confirm → reserved 100→0 (spent), active stays 400, PointsRedeemed = 100
        var confirmed = await fx.Post($"/api/v1/redemptions/{redemptionId}/confirm", new { }, auth);
        confirmed.GetProperty("status").GetString().ShouldBe("Confirmed");
        var w2 = await fx.Get($"/api/v1/members/{memberId}/wallet", auth);
        w2.GetProperty("activeBalance").GetInt64().ShouldBe(400);
        w2.GetProperty("reservedBalance").GetInt64().ShouldBe(0);
        (await fx.Get($"/api/v1/members/{memberId}/value-profile", auth)).GetProperty("pointsRedeemed").GetInt64().ShouldBe(100);

        // reserve again then cancel → points released back to active (400→300→400)
        var r2 = await fx.Post($"/api/v1/members/{memberId}/redemptions", new { rewardId }, auth);
        (await fx.Get($"/api/v1/members/{memberId}/wallet", auth)).GetProperty("activeBalance").GetInt64().ShouldBe(300);
        var cancelled = await fx.Post($"/api/v1/redemptions/{r2.GetProperty("id").GetString()}/cancel", new { }, auth);
        cancelled.GetProperty("status").GetString().ShouldBe("Cancelled");
        var w3 = await fx.Get($"/api/v1/members/{memberId}/wallet", auth);
        w3.GetProperty("activeBalance").GetInt64().ShouldBe(400);
        w3.GetProperty("reservedBalance").GetInt64().ShouldBe(0);
    }

    [Fact]
    public async Task Reserving_more_than_balance_is_rejected()
    {
        var (appId, apiKey) = await fx.OnboardChildAsync("redeem-insufficient");
        var auth = ApiFixture.Auth(apiKey);
        await fx.Put($"/api/v1/applications/{appId}/loyalty-settings", new
        {
            pointsUnitCode = "POINT", baseCurrency = "IQD", spendAmountPerPoint = 1000, roundingPolicy = "Floor", loyaltyStatus = "Active",
        }, auth);
        var member = await fx.Post("/api/v1/members", new { externalCustomerId = "broke" }, auth);
        var memberId = member.GetProperty("memberId").GetString()!;
        var reward = await fx.Post("/api/v1/rewards", new { name = "Big", pointsCost = 1000, rewardType = "Voucher" }, auth);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/members/{memberId}/redemptions")
        { Content = JsonContent.Create(new { rewardId = reward.GetProperty("id").GetString() }) };
        req.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
        (await fx.Client.SendAsync(req)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
