using System.Linq;
using Shouldly;

namespace Loyalty.Tests.Integration;

/// <summary>
/// Whole-system golden path: a single tenant configures every module, then a realistic multi-member
/// lifecycle flows through the platform end to end — registration, purchases (base earn + tier
/// multiplier + achievement + stamp + raffle entry), login streak, app-share grant, referral,
/// birthday, RFM, redemption, refund reversal, points expiry, a raffle draw, the customer app, and
/// reporting. Asserts the integrated state and that every major result event was emitted.
/// Complements the focused per-feature suites with one cross-cutting integration proof.
/// </summary>
[Collection("api")]
public class FullJourneyE2ETests(ApiFixture fx)
{
    private Dictionary<string, string> _auth = default!;
    private static Dictionary<string, string> Sub(Dictionary<string, string> auth, string k) =>
        new() { ["X-Api-Key"] = auth["X-Api-Key"], ["Idempotency-Key"] = k };
    private static Dictionary<string, string> Bearer(string token) => new() { ["Authorization"] = $"Bearer {token}" };

    private async Task Coffee(string order, string customer, int amount, int daysAgo = 0) =>
        await Emit(order, new
        {
            eventId = Guid.NewGuid(), eventType = "PurchaseCompleted", eventVersion = 1, sourceTransactionId = order,
            customerId = customer, occurredAt = DateTimeOffset.UtcNow.AddDays(-daysAgo), correlationId = order,
            attributes = new { orderId = order, amount, currency = "IQD", service = "coffee" },
        });

    private async Task<System.Text.Json.JsonElement> Emit(string idem, object evt)
    {
        var submit = await fx.Post("/api/v1/events", evt, Sub(_auth, idem));
        return await fx.PollRequest(submit.GetProperty("requestId").GetString()!, _auth);
    }
    private Task<System.Text.Json.JsonElement> Member(string ext) => fx.Get($"/api/v1/members/by-external-id/{ext}", _auth);
    private async Task<System.Text.Json.JsonElement> Profile(string ext) =>
        await fx.Get($"/api/v1/members/{(await Member(ext)).GetProperty("memberId").GetString()}/value-profile", _auth);

    [Fact]
    public async Task Entire_platform_runs_end_to_end()
    {
        var (appId, apiKey) = await fx.OnboardChildAsync("journey");
        _auth = ApiFixture.Auth(apiKey);
        var auth = _auth;

        // ---------- configure every module ----------
        await fx.Put($"/api/v1/applications/{appId}/loyalty-settings", new
        {
            pointsUnitCode = "POINT", baseCurrency = "IQD", spendAmountPerPoint = 1000,
            roundingPolicy = "Floor", loyaltyStatus = "Active", pointsExpiryDays = 30,
        }, auth);

        var baseEarn = await fx.Post("/api/v1/campaigns", new
        {
            name = "Base Earn", triggerEventType = "PurchaseCompleted", minimumEventVersion = 1, priority = 100,
            stackingPolicy = "Stackable", conditions = Array.Empty<object>(),
            effects = new[] { new { effectId = "base-earn", type = "AwardBasePoints" } },
        }, auth);
        await fx.ActivateCampaign(baseEarn.GetProperty("id").GetString()!, auth);

        await fx.Post("/api/v1/tiers", new { name = "Bronze", rank = 0, thresholdMetric = "NetSpend", thresholdValue = 0, earnMultiplier = 1.0 }, auth);
        await fx.Post("/api/v1/tiers", new { name = "Silver", rank = 1, thresholdMetric = "NetSpend", thresholdValue = 100000, earnMultiplier = 1.5 }, auth);

        var coffeeReward = await fx.Post("/api/v1/rewards", new { name = "Free Coffee", nameAr = "قهوة مجانية", pointsCost = 100, rewardType = "Discount", value = 2000 }, auth);
        var welcomeReward = await fx.Post("/api/v1/rewards", new { name = "Welcome Voucher", pointsCost = 1, rewardType = "Voucher", value = 1000 }, auth);

        await fx.Post("/api/v1/achievements", new { name = "First Purchase", nameAr = "أول شراء", metric = "PurchaseCount", threshold = 1, bonusPoints = 50 }, auth);
        await fx.Post("/api/v1/stamp-cards", new { name = "Coffee card", stampsRequired = 3, rewardPoints = 200, triggerEventType = "PurchaseCompleted", conditions = new[] { new { field = "attributes.service", @operator = "Equals", value = "coffee" } } }, auth);
        await fx.Put($"/api/v1/applications/{appId}/engagement-settings", new { referralEnabled = true, referrerPoints = 100, refereePoints = 50, birthdayEnabled = true, birthdayBonusPoints = 500 }, auth);
        var raffle = await fx.Post("/api/v1/raffles", new { name = "Grand Draw", triggerEventType = "PurchaseCompleted", minAmount = 50000, entriesPerEvent = 1, prizePoints = 5000, prizeDescription = "iPhone", conditions = Array.Empty<object>() }, auth);
        var raffleId = raffle.GetProperty("id").GetString()!;

        // share-the-app → grant a welcome voucher
        var shareCampaign = await fx.Post("/api/v1/campaigns", new
        {
            name = "Share gift", triggerEventType = "CustomAction", minimumEventVersion = 1, priority = 50, stackingPolicy = "Stackable",
            conditions = new[] { new { field = "attributes.action", @operator = "Equals", value = "app.shared" } },
            effects = new[] { new { effectId = "gift", type = "GrantReward", rewardId = welcomeReward.GetProperty("id").GetString() } },
        }, auth);
        await fx.ActivateCampaign(shareCampaign.GetProperty("id").GetString()!, auth);

        // ---------- lifecycle ----------
        // alice registers + three coffee purchases (base earn, achievement, stamp card, tier up, raffle entries)
        await Emit("reg-alice", new { eventId = Guid.NewGuid(), eventType = "CustomerRegistered", eventVersion = 1, sourceTransactionId = "reg-alice", customerId = "alice", occurredAt = DateTimeOffset.UtcNow, correlationId = "reg-alice", attributes = new { } });
        await Coffee("a1", "alice", 60000);
        await Coffee("a2", "alice", 60000);
        await Coffee("a3", "alice", 60000);

        var aliceProfile = await Profile("alice");
        aliceProfile.GetProperty("purchaseCount").GetInt64().ShouldBe(3);
        aliceProfile.GetProperty("tier").GetProperty("rank").GetInt32().ShouldBe(1); // Silver (netSpend 180k ≥ 100k)
        aliceProfile.GetProperty("pointsEarned").GetInt64().ShouldBeGreaterThan(200); // base + achievement + stamp
        var aliceId = (await Member("alice")).GetProperty("memberId").GetString()!;
        (await fx.Get($"/api/v1/members/{aliceId}/achievements", auth)).GetProperty("items").EnumerateArray()
            .Any(a => a.GetProperty("name").GetString() == "First Purchase").ShouldBeTrue();
        (await fx.Get($"/api/v1/members/{aliceId}/stamp-cards", auth)).GetProperty("items").EnumerateArray()
            .First().GetProperty("completedCount").GetInt32().ShouldBe(1);
        (await fx.Get($"/api/v1/raffles/{raffleId}/entries", auth)).GetProperty("items").EnumerateArray()
            .First(e => e.GetProperty("externalCustomerId").GetString() == "alice").GetProperty("entries").GetInt64().ShouldBe(3);

        // alice logs in → streak; shares the app → granted welcome voucher
        await Emit("login-alice", new { eventId = Guid.NewGuid(), eventType = "ApplicationLogin", eventVersion = 1, sourceTransactionId = "login-alice", customerId = "alice", occurredAt = DateTimeOffset.UtcNow, correlationId = "login-alice", attributes = new { channel = "mobile" } });
        (await Profile("alice")).GetProperty("currentStreak").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        await Emit("share-alice", new { eventId = Guid.NewGuid(), eventType = "CustomAction", eventVersion = 1, sourceTransactionId = "share-alice", customerId = "alice", occurredAt = DateTimeOffset.UtcNow, correlationId = "share-alice", attributes = new { action = "app.shared", actionId = "share-alice-1" } });
        (await fx.Get($"/api/v1/members/{aliceId}/redemptions", auth)).GetProperty("items").EnumerateArray()
            .Any(r => r.GetProperty("rewardName").GetString() == "Welcome Voucher" && r.GetProperty("pointsCost").GetInt64() == 0).ShouldBeTrue();

        // alice refers bob → both rewarded
        var aliceCode = (await Member("alice")).GetProperty("referralCode").GetString()!;
        await Emit("ref-bob", new { eventId = Guid.NewGuid(), eventType = "ReferralCompleted", eventVersion = 1, sourceTransactionId = "ref-bob", customerId = "bob", occurredAt = DateTimeOffset.UtcNow, correlationId = "ref-bob", attributes = new { referralId = "ref-bob", referrerCode = aliceCode } });
        (await Member("bob")).GetProperty("wallet").GetProperty("activeBalance").GetInt64().ShouldBe(50);
        (await Profile("alice")).GetProperty("referralCount").GetInt64().ShouldBe(1);

        // carol has a birthday today → birthday sweep credits her
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await fx.Post("/api/v1/members", new { externalCustomerId = "carol", birthDate = new DateOnly(1990, today.Month, today.Day).ToString("yyyy-MM-dd") }, auth);
        (await fx.Post("/api/v1/admin/birthday-sweep", new { }, auth)).GetProperty("rewarded").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        (await Member("carol")).GetProperty("wallet").GetProperty("activeBalance").GetInt64().ShouldBe(500);

        // RFM recompute → alice gets a persona
        (await fx.Post("/api/v1/admin/rfm-recompute", new { }, auth)).GetProperty("scored").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        (await Profile("alice")).GetProperty("rfm").GetProperty("persona").GetString().ShouldNotBeNullOrEmpty();

        // alice redeems Free Coffee (reserve → confirm): 100 points leave the wallet
        var beforeRedeem = (await fx.Get($"/api/v1/members/{aliceId}/wallet", auth)).GetProperty("activeBalance").GetInt64();
        var redemption = await fx.Post($"/api/v1/members/{aliceId}/redemptions", new { rewardId = coffeeReward.GetProperty("id").GetString() }, auth);
        redemption.GetProperty("couponCode").GetString().ShouldNotBeNullOrWhiteSpace();
        await fx.Post($"/api/v1/redemptions/{redemption.GetProperty("id").GetString()}/confirm", new { }, auth);
        (await Profile("alice")).GetProperty("pointsRedeemed").GetInt64().ShouldBe(100);
        (await fx.Get($"/api/v1/members/{aliceId}/wallet", auth)).GetProperty("activeBalance").GetInt64().ShouldBe(beforeRedeem - 100);

        // refund alice's first coffee → points reversed + a stamp unwound
        await Emit("rf-a1", new { eventId = Guid.NewGuid(), eventType = "OrderRefunded", eventVersion = 1, sourceTransactionId = "rf-a1", customerId = "alice", occurredAt = DateTimeOffset.UtcNow, correlationId = "rf-a1", attributes = new { refundId = "rf-a1", originalOrderId = "a1", amount = 60000, currency = "IQD" } });

        // dave: an old small purchase that later expires (below raffle threshold, so not in the draw).
        // Earns 40 base + 50 "First Purchase" achievement, all dated 60 days ago → all expire.
        await Coffee("d1", "dave", 40000, daysAgo: 60);
        var daveId = (await Member("dave")).GetProperty("memberId").GetString()!;
        var davePre = (await fx.Get($"/api/v1/members/{daveId}/wallet", auth)).GetProperty("activeBalance").GetInt64();
        davePre.ShouldBe(90);
        (await fx.Post("/api/v1/admin/expire-points", new { }, auth)).GetProperty("expired").GetInt64().ShouldBeGreaterThanOrEqualTo(davePre);
        (await fx.Get($"/api/v1/members/{daveId}/wallet", auth)).GetProperty("activeBalance").GetInt64().ShouldBe(0);

        // draw the raffle → alice (only eligible entrant) wins + gets the prize
        var drawn = await fx.Post($"/api/v1/raffles/{raffleId}/draw", new { }, auth);
        drawn.GetProperty("status").GetString().ShouldBe("Drawn");
        drawn.GetProperty("winnerMemberId").GetString().ShouldBe(aliceId);

        // ---------- customer app (member token, own data only) ----------
        var token = (await fx.Post($"/api/v1/members/{aliceId}/access-token", new { }, auth)).GetProperty("token").GetString()!;
        var me = await fx.Get("/api/v1/customer/me", Bearer(token));
        me.GetProperty("member").GetProperty("externalCustomerId").GetString().ShouldBe("alice");
        me.GetProperty("profile").GetProperty("tier").GetProperty("rank").GetInt32().ShouldBe(1);
        (await fx.Get("/api/v1/customer/raffles", Bearer(token))).GetProperty("items").EnumerateArray()
            .Any(r => r.GetProperty("won").GetBoolean()).ShouldBeTrue();

        // ---------- reporting ----------
        var promo = await fx.Get("/api/v1/analytics/promotions", auth);
        promo.GetProperty("campaigns").EnumerateArray().First(c => c.GetProperty("name").GetString() == "Base Earn").GetProperty("executions").GetInt32().ShouldBeGreaterThanOrEqualTo(2);
        promo.GetProperty("rewards").EnumerateArray().Any(r => r.GetProperty("confirmed").GetInt32() >= 1).ShouldBeTrue();
        promo.GetProperty("liabilities").GetProperty("outstandingPoints").GetInt64().ShouldBeGreaterThan(0);
        (await fx.Get("/api/v1/analytics/overview", auth)).GetProperty("members").GetInt32().ShouldBeGreaterThanOrEqualTo(4);

        // ---------- every major result event fired ----------
        var feed = await fx.Get("/api/v1/notifications?limit=500", auth);
        var types = feed.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("eventType").GetString()).ToHashSet();
        foreach (var expected in new[]
        {
            "PointsAwardedV1", "MemberTierChangedV1", "AchievementUnlockedV1", "StampCardCompletedV1",
            "ReferralRewardedV1", "BirthdayRewardedV1", "RaffleEntryGrantedV1", "RaffleWonV1",
            "RewardGrantedV1", "PointsRedeemedV1", "PointsReversedV1", "PointsExpiredV1",
        })
            types.ShouldContain(expected);
    }
}
