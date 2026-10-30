using Shouldly;

namespace Loyalty.Tests.Integration;

/// <summary>
/// Phase-10 engagement: referral program rewards both the referrer and the referee (idempotent per
/// referralId), a stamp card awards + resets when it fills, and the birthday sweep credits a member
/// once per year.
/// </summary>
[Collection("api")]
public class EngagementE2ETests(ApiFixture fx)
{
    private static Dictionary<string, string> Sub(Dictionary<string, string> auth, string k) =>
        new() { ["X-Api-Key"] = auth["X-Api-Key"], ["Idempotency-Key"] = k };

    private static object Purchase(string orderId, string customer, int amount) => new
    {
        eventId = Guid.NewGuid(), eventType = "PurchaseCompleted", eventVersion = 1,
        sourceTransactionId = orderId, customerId = customer, occurredAt = DateTimeOffset.UtcNow,
        correlationId = orderId, attributes = new { orderId, amount, currency = "IQD" },
    };

    private async Task<(string appId, Dictionary<string, string> auth)> ConfiguredApp(string name, bool withBaseEarn)
    {
        var (appId, apiKey) = await fx.OnboardChildAsync(name);
        var auth = ApiFixture.Auth(apiKey);
        await fx.Put($"/api/v1/applications/{appId}/loyalty-settings", new
        {
            pointsUnitCode = "POINT", baseCurrency = "IQD", spendAmountPerPoint = 1000, roundingPolicy = "Floor", loyaltyStatus = "Active",
        }, auth);
        if (withBaseEarn)
        {
            var c = await fx.Post("/api/v1/campaigns", new
            {
                name = "Base Earn", triggerEventType = "PurchaseCompleted", minimumEventVersion = 1,
                priority = 100, stackingPolicy = "Stackable", conditions = Array.Empty<object>(),
                effects = new[] { new { effectId = "base-earn", type = "AwardBasePoints" } },
            }, auth);
            await fx.ActivateCampaign(c.GetProperty("id").GetString()!, auth);
        }
        return (appId, auth);
    }

    [Fact]
    public async Task Referral_rewards_both_sides_once_per_referral()
    {
        var (appId, auth) = await ConfiguredApp("ref", withBaseEarn: false);
        await fx.Put($"/api/v1/applications/{appId}/engagement-settings", new
        {
            referralEnabled = true, referrerPoints = 100, refereePoints = 50, birthdayEnabled = false, birthdayBonusPoints = 0,
        }, auth);

        // the referrer is an existing member with a shareable code
        var referrer = await fx.Post("/api/v1/members", new { externalCustomerId = "alice" }, auth);
        var code = referrer.GetProperty("referralCode").GetString()!;
        code.ShouldNotBeNullOrWhiteSpace();
        var referrerId = referrer.GetProperty("memberId").GetString()!;

        // a referral completes for the new customer "bob", crediting it to alice's code
        object Ref(string eventId, string idem) => new
        {
            eventId, eventType = "ReferralCompleted", eventVersion = 1, sourceTransactionId = "r1",
            customerId = "bob", occurredAt = DateTimeOffset.UtcNow, correlationId = "r1",
            attributes = new { referralId = "r1", referrerCode = code },
        };

        var submit = await fx.Post("/api/v1/events", Ref(Guid.NewGuid().ToString(), "ref-1"), Sub(auth, "ref-1"));
        var final = await fx.PollRequest(submit.GetProperty("requestId").GetString()!, auth);
        final.GetProperty("status").GetString().ShouldBe("Completed");
        final.GetProperty("awardedPoints").GetInt64().ShouldBe(50); // referee bonus

        var bob = await fx.Get("/api/v1/members/by-external-id/bob", auth);
        bob.GetProperty("wallet").GetProperty("activeBalance").GetInt64().ShouldBe(50);

        var alice = await fx.Get($"/api/v1/members/{referrerId}", auth);
        alice.GetProperty("wallet").GetProperty("activeBalance").GetInt64().ShouldBe(100);
        var aliceProfile = await fx.Get($"/api/v1/members/{referrerId}/value-profile", auth);
        aliceProfile.GetProperty("referralCount").GetInt64().ShouldBe(1);

        // same referralId again (new eventId) → no double reward
        var dup = await fx.Post("/api/v1/events", Ref(Guid.NewGuid().ToString(), "ref-2"), Sub(auth, "ref-2"));
        await fx.PollRequest(dup.GetProperty("requestId").GetString()!, auth);
        (await fx.Get($"/api/v1/members/{referrerId}", auth)).GetProperty("wallet").GetProperty("activeBalance").GetInt64().ShouldBe(100);

        var feed = await fx.Get("/api/v1/notifications", auth);
        feed.GetProperty("items").EnumerateArray().Any(i => i.GetProperty("eventType").GetString() == "ReferralRewardedV1").ShouldBeTrue();
    }

    [Fact]
    public async Task Stamp_card_awards_and_resets_when_full()
    {
        var (_, auth) = await ConfiguredApp("stamp", withBaseEarn: true);
        await fx.Post("/api/v1/stamp-cards", new
        {
            name = "Buy 3 get bonus", nameAr = "اشترِ ٣", stampsRequired = 3, rewardPoints = 200, triggerEventType = "PurchaseCompleted",
        }, auth);

        // base earn = 1 point per 1000 IQD; 3rd purchase fills the card → +200
        (await fx.PollRequest((await fx.Post("/api/v1/events", Purchase("S1", "carol", 1000), Sub(auth, "s1"))).GetProperty("requestId").GetString()!, auth))
            .GetProperty("awardedPoints").GetInt64().ShouldBe(1);
        (await fx.PollRequest((await fx.Post("/api/v1/events", Purchase("S2", "carol", 1000), Sub(auth, "s2"))).GetProperty("requestId").GetString()!, auth))
            .GetProperty("awardedPoints").GetInt64().ShouldBe(1);
        (await fx.PollRequest((await fx.Post("/api/v1/events", Purchase("S3", "carol", 1000), Sub(auth, "s3"))).GetProperty("requestId").GetString()!, auth))
            .GetProperty("awardedPoints").GetInt64().ShouldBe(201); // base 1 + stamp reward 200

        var m = await fx.Get("/api/v1/members/by-external-id/carol", auth);
        var memberId = m.GetProperty("memberId").GetString()!;
        var progress = await fx.Get($"/api/v1/members/{memberId}/stamp-cards", auth);
        var card = progress.GetProperty("items").EnumerateArray().First();
        card.GetProperty("completedCount").GetInt32().ShouldBe(1);
        card.GetProperty("stamps").GetInt32().ShouldBe(0); // reset after completing

        var feed = await fx.Get("/api/v1/notifications", auth);
        feed.GetProperty("items").EnumerateArray().Any(i => i.GetProperty("eventType").GetString() == "StampCardCompletedV1").ShouldBeTrue();
    }

    [Fact]
    public async Task Birthday_sweep_rewards_member_once_per_year()
    {
        var (appId, auth) = await ConfiguredApp("bday", withBaseEarn: false);
        await fx.Put($"/api/v1/applications/{appId}/engagement-settings", new
        {
            referralEnabled = false, referrerPoints = 0, refereePoints = 0, birthdayEnabled = true, birthdayBonusPoints = 500,
        }, auth);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var bd = new DateOnly(2000, today.Month, today.Day).ToString("yyyy-MM-dd");
        var member = await fx.Post("/api/v1/members", new { externalCustomerId = "dave", birthDate = bd }, auth);
        var memberId = member.GetProperty("memberId").GetString()!;

        var r1 = await fx.Post("/api/v1/admin/birthday-sweep", new { }, auth);
        r1.GetProperty("rewarded").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        (await fx.Get($"/api/v1/members/{memberId}", auth)).GetProperty("wallet").GetProperty("activeBalance").GetInt64().ShouldBe(500);

        // running again the same year does not re-reward dave
        await fx.Post("/api/v1/admin/birthday-sweep", new { }, auth);
        (await fx.Get($"/api/v1/members/{memberId}", auth)).GetProperty("wallet").GetProperty("activeBalance").GetInt64().ShouldBe(500);
    }
}
