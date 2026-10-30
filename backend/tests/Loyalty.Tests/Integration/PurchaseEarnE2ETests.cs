using Shouldly;

namespace Loyalty.Tests.Integration;

/// <summary>
/// Phase-1 release gate: settings → member → active purchase-earn campaign → PurchaseCompleted
/// event → points in the wallet, plus HTTP idempotency (same key → same request, no double award).
/// </summary>
[Collection("api")]
public class PurchaseEarnE2ETests(ApiFixture fx)
{
    [Fact]
    public async Task Purchase_event_earns_points_end_to_end()
    {
        var (appId, apiKey) = await fx.OnboardChildAsync("purchase-earn");
        var auth = ApiFixture.Auth(apiKey);

        await fx.Put($"/api/v1/applications/{appId}/loyalty-settings", new
        {
            pointsUnitCode = "POINT", baseCurrency = "IQD", spendAmountPerPoint = 1000,
            roundingPolicy = "Floor", loyaltyStatus = "Active",
        }, auth);

        var member = await fx.Post("/api/v1/members", new { externalCustomerId = "user-1" }, auth);
        var memberId = member.GetProperty("memberId").GetString()!;

        var campaign = await fx.Post("/api/v1/campaigns", new
        {
            name = "Base Earn", nameAr = "نقاط الشراء", triggerEventType = "PurchaseCompleted",
            minimumEventVersion = 1, priority = 100, stackingPolicy = "Stackable",
            conditions = Array.Empty<object>(), effects = new[] { new { effectId = "base-earn", type = "AwardBasePoints" } },
        }, auth);
        var campaignId = campaign.GetProperty("id").GetString()!;
        await fx.Post($"/api/v1/campaigns/{campaignId}/activate", new { }, auth);

        var envelope = new
        {
            eventId = Guid.NewGuid(), eventType = "PurchaseCompleted", eventVersion = 1,
            sourceTransactionId = "ORDER-1", customerId = "user-1", occurredAt = DateTimeOffset.UtcNow,
            correlationId = "corr-1", attributes = new { orderId = "ORDER-1", amount = 50000, currency = "IQD" },
        };
        var submitAuth = new Dictionary<string, string> { ["X-Api-Key"] = apiKey, ["Idempotency-Key"] = "order-1-key" };
        var submit = await fx.Post("/api/v1/events", envelope, submitAuth);
        var requestId = submit.GetProperty("requestId").GetString()!;

        var final = await fx.PollRequest(requestId, auth);
        final.GetProperty("status").GetString().ShouldBe("Completed");
        final.GetProperty("awardedPoints").GetInt64().ShouldBe(50);

        var wallet = await fx.Get($"/api/v1/members/{memberId}/wallet", auth);
        wallet.GetProperty("activeBalance").GetInt64().ShouldBe(50);

        // idempotency: same key → same request id, no double award
        var replay = await fx.Post("/api/v1/events", envelope, submitAuth);
        replay.GetProperty("requestId").GetString().ShouldBe(requestId);
        var wallet2 = await fx.Get($"/api/v1/members/{memberId}/wallet", auth);
        wallet2.GetProperty("activeBalance").GetInt64().ShouldBe(50);
    }
}
