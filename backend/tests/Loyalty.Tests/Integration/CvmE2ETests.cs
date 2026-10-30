using Shouldly;

namespace Loyalty.Tests.Integration;

/// <summary>
/// Phase-2 CVM gate: value profile is built from events, a segment is activated and entered,
/// and a segment-targeted campaign only rewards members in the required segment. Also proves
/// CustomerRegistered → welcome points via a registration campaign.
/// </summary>
[Collection("api")]
public class CvmE2ETests(ApiFixture fx)
{
    [Fact]
    public async Task Value_profile_segments_and_targeted_campaign()
    {
        var (appId, apiKey) = await fx.OnboardChildAsync("cvm");
        var auth = ApiFixture.Auth(apiKey);

        await fx.Put($"/api/v1/applications/{appId}/loyalty-settings", new
        {
            pointsUnitCode = "POINT", baseCurrency = "IQD", spendAmountPerPoint = 1000,
            roundingPolicy = "Floor", loyaltyStatus = "Active",
        }, auth);
        await fx.Put($"/api/v1/applications/{appId}/cvm-settings", new
        {
            cvmStatus = "Active", newPeriodDays = 30, atRiskAfterDays = 30, dormantAfterDays = 90,
            churnedAfterDays = 180, highValueNetSpendThreshold = 1000000, frequentPurchaseThreshold = 5, evaluationWindowDays = 365,
        }, auth);

        var member = await fx.Post("/api/v1/members", new { externalCustomerId = "cust-1" }, auth);
        var memberId = member.GetProperty("memberId").GetString()!;

        // segment: New customers
        var seg = await fx.Post("/api/v1/segments", new
        {
            name = "New Customer", nameAr = "عميل جديد",
            conditions = new[] { new { field = "lifecycleStage", @operator = "Equals", value = "New" } },
        }, auth);
        var segId = seg.GetProperty("id").GetString()!;
        await fx.Post($"/api/v1/segments/{segId}/validate", new { }, auth);
        var activated = await fx.Post($"/api/v1/segments/{segId}/activate", new { }, auth);
        activated.GetProperty("status").GetString().ShouldBe("Active");

        // segment-targeted campaign: only rewards members in "New Customer"
        var targeted = await fx.Post("/api/v1/campaigns", new
        {
            name = "New Customer Earn", triggerEventType = "PurchaseCompleted", minimumEventVersion = 1,
            priority = 100, stackingPolicy = "Stackable",
            conditions = Array.Empty<object>(),
            effects = new[] { new { effectId = "base-earn", type = "AwardBasePoints" } },
            requiredSegmentIds = new[] { segId },
        }, auth);
        await fx.ActivateCampaign(targeted.GetProperty("id").GetString()!, auth);

        // high-value-only bonus campaign: member is NOT high value → must be gated out
        var highValueSeg = await fx.Post("/api/v1/segments", new
        {
            name = "High Value",
            conditions = new[] { new { field = "netSpendAmount", @operator = "GreaterThanOrEqual", value = "1000000" } },
        }, auth);
        var hvId = highValueSeg.GetProperty("id").GetString()!;
        await fx.Post($"/api/v1/segments/{hvId}/activate", new { }, auth);
        var bonus = await fx.Post("/api/v1/campaigns", new
        {
            name = "High Value Bonus", triggerEventType = "PurchaseCompleted", minimumEventVersion = 1,
            priority = 200, stackingPolicy = "Stackable",
            conditions = Array.Empty<object>(),
            effects = new[] { new { effectId = "hv-bonus", type = "AwardPoints", points = 999 } },
            requiredSegmentIds = new[] { hvId },
        }, auth);
        await fx.ActivateCampaign(bonus.GetProperty("id").GetString()!, auth);

        // submit a purchase: member is New (in New Customer) but not High Value
        var submitAuth = new Dictionary<string, string> { ["X-Api-Key"] = apiKey, ["Idempotency-Key"] = "cvm-order-1" };
        var submit = await fx.Post("/api/v1/events", new
        {
            eventId = Guid.NewGuid(), eventType = "PurchaseCompleted", eventVersion = 1,
            sourceTransactionId = "ORDER-1", customerId = "cust-1", occurredAt = DateTimeOffset.UtcNow,
            correlationId = "c1", attributes = new { orderId = "ORDER-1", amount = 50000, currency = "IQD" },
        }, submitAuth);
        var final = await fx.PollRequest(submit.GetProperty("requestId").GetString()!, auth);
        final.GetProperty("status").GetString().ShouldBe("Completed");
        // base earn 50 only; the 999 high-value bonus is gated out by audience
        final.GetProperty("awardedPoints").GetInt64().ShouldBe(50);

        // value profile reflects the purchase + New Customer membership
        var profile = await fx.Get($"/api/v1/members/{memberId}/value-profile", auth);
        profile.GetProperty("lifecycleStage").GetString().ShouldBe("New");
        profile.GetProperty("purchaseCount").GetInt64().ShouldBe(1);
        profile.GetProperty("netSpendAmount").GetDecimal().ShouldBe(50000);
        profile.GetProperty("pointsEarned").GetInt64().ShouldBe(50);
        profile.GetProperty("activeSegments").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Customer_registered_awards_welcome_points()
    {
        var (appId, apiKey) = await fx.OnboardChildAsync("welcome");
        var auth = ApiFixture.Auth(apiKey);

        await fx.Put($"/api/v1/applications/{appId}/loyalty-settings", new
        {
            pointsUnitCode = "POINT", baseCurrency = "IQD", spendAmountPerPoint = 1000,
            roundingPolicy = "Floor", loyaltyStatus = "Active",
        }, auth);

        var welcome = await fx.Post("/api/v1/campaigns", new
        {
            name = "Welcome", nameAr = "ترحيب", triggerEventType = "CustomerRegistered", minimumEventVersion = 1,
            priority = 100, stackingPolicy = "Stackable", conditions = Array.Empty<object>(),
            effects = new[] { new { effectId = "welcome-100", type = "AwardPoints", points = 100 } },
        }, auth);
        await fx.ActivateCampaign(welcome.GetProperty("id").GetString()!, auth);

        // register a brand-new customer (member auto-provisioned by the worker)
        var submitAuth = new Dictionary<string, string> { ["X-Api-Key"] = apiKey, ["Idempotency-Key"] = "reg-new-1" };
        var submit = await fx.Post("/api/v1/events", new
        {
            eventId = Guid.NewGuid(), eventType = "CustomerRegistered", eventVersion = 1,
            sourceTransactionId = "newcust", customerId = "newcust", occurredAt = DateTimeOffset.UtcNow,
            correlationId = "r1", attributes = new { registrationChannel = "app" },
        }, submitAuth);
        var final = await fx.PollRequest(submit.GetProperty("requestId").GetString()!, auth);
        final.GetProperty("status").GetString().ShouldBe("Completed");
        final.GetProperty("awardedPoints").GetInt64().ShouldBe(100);

        var m = await fx.Get("/api/v1/members/by-external-id/newcust", auth);
        var wallet = await fx.Get($"/api/v1/members/{m.GetProperty("memberId").GetString()}/wallet", auth);
        wallet.GetProperty("activeBalance").GetInt64().ShouldBe(100);
    }
}
