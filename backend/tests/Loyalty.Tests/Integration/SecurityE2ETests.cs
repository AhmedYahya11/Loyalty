using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;

namespace Loyalty.Tests.Integration;

/// <summary>
/// Security controls, exercised against the real pipeline: authentication (API key + member token),
/// tenant isolation (the closure-scope query filter + X-Application-Id subtree check), bootstrap
/// protection, replay/idempotency, input-size + rate limits, and secret non-exposure.
/// </summary>
[Collection("api")]
public class SecurityE2ETests(ApiFixture fx)
{
    private static Dictionary<string, string> Key(string k) => new() { ["X-Api-Key"] = k };
    private static Dictionary<string, string> Bearer(string t) => new() { ["Authorization"] = $"Bearer {t}" };

    private async Task<HttpResponseMessage> Raw(HttpMethod method, string url, Dictionary<string, string>? headers = null, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        if (headers is not null) foreach (var (k, v) in headers) req.Headers.TryAddWithoutValidation(k, v);
        return await fx.Client.SendAsync(req);
    }

    private async Task Configure(string appId, Dictionary<string, string> auth)
    {
        await fx.Put($"/api/v1/applications/{appId}/loyalty-settings", new
        {
            pointsUnitCode = "POINT", baseCurrency = "IQD", spendAmountPerPoint = 1000, roundingPolicy = "Floor", loyaltyStatus = "Active",
        }, auth);
        var c = await fx.Post("/api/v1/campaigns", new
        {
            name = "Base Earn", triggerEventType = "PurchaseCompleted", minimumEventVersion = 1, priority = 100,
            stackingPolicy = "Stackable", conditions = Array.Empty<object>(),
            effects = new[] { new { effectId = "base-earn", type = "AwardBasePoints" } },
        }, auth);
        await fx.ActivateCampaign(c.GetProperty("id").GetString()!, auth);
    }

    // ---------- authentication ----------
    [Fact]
    public async Task Business_endpoints_reject_missing_or_invalid_api_key()
    {
        (await Raw(HttpMethod.Get, "/api/v1/campaigns")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Raw(HttpMethod.Get, "/api/v1/campaigns", Key("lk_live_not_a_real_key"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Raw(HttpMethod.Post, "/api/v1/members", Key("nope"), new { externalCustomerId = "x" })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Revoked_api_key_stops_working()
    {
        var (appId, apiKey) = await fx.OnboardChildAsync("sec-revoke");
        // a freshly created second account on the app, then revoked
        var created = await fx.Post($"/api/v1/applications/{appId}/accounts", new { accountName = "temp" }, Key(fx.RootKey));
        var tempKey = created.GetProperty("apiKey").GetString()!;
        var acctId = created.GetProperty("accountId").GetString()!;
        (await Raw(HttpMethod.Get, "/api/v1/campaigns", Key(tempKey))).StatusCode.ShouldBe(HttpStatusCode.OK);

        await fx.Post($"/api/v1/accounts/{acctId}/revoke", new { }, Key(fx.RootKey));
        (await Raw(HttpMethod.Get, "/api/v1/campaigns", Key(tempKey))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---------- tenant isolation ----------
    [Fact]
    public async Task One_tenant_cannot_read_or_target_another_tenants_data()
    {
        var (appA, keyA) = await fx.OnboardChildAsync("sec-tenant-a");
        var (appB, keyB) = await fx.OnboardChildAsync("sec-tenant-b");
        var a = Key(keyA); var b = Key(keyB);
        await Configure(appA, a);
        await fx.Post("/api/v1/members", new { externalCustomerId = "iso-alice" }, a);
        await fx.Post("/api/v1/campaigns", new { name = "A-SECRET", triggerEventType = "PurchaseCompleted", minimumEventVersion = 1, priority = 1, stackingPolicy = "Stackable", conditions = Array.Empty<object>(), effects = new[] { new { effectId = "x", type = "AwardBasePoints" } } }, a);

        // B cannot see A's member or campaign
        (await Raw(HttpMethod.Get, "/api/v1/members/by-external-id/iso-alice", b)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var bCampaigns = await fx.Get("/api/v1/campaigns", b);
        bCampaigns.EnumerateArray().Any(c => c.GetProperty("name").GetString() == "A-SECRET").ShouldBeFalse();

        // B cannot target A's application via X-Application-Id (outside its subtree) → 403
        var cross = await Raw(HttpMethod.Get, "/api/v1/campaigns", new() { ["X-Api-Key"] = keyB, ["X-Application-Id"] = appA });
        cross.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // B cannot create data inside A's application either
        var crossWrite = await Raw(HttpMethod.Post, "/api/v1/members", new() { ["X-Api-Key"] = keyB, ["X-Application-Id"] = appA }, new { externalCustomerId = "intruder" });
        crossWrite.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // the parent (root) CAN see its descendant's member — that's the intended hierarchy
        (await Raw(HttpMethod.Get, "/api/v1/members/by-external-id/iso-alice", Key(fx.RootKey))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---------- customer token scope ----------
    [Fact]
    public async Task Member_token_cannot_reach_admin_endpoints_and_rejects_bad_tokens()
    {
        var (appId, apiKey) = await fx.OnboardChildAsync("sec-cust");
        var auth = Key(apiKey);
        await Configure(appId, auth);
        var member = await fx.Post("/api/v1/members", new { externalCustomerId = "cust-sec" }, auth);
        var token = (await fx.Post($"/api/v1/members/{member.GetProperty("memberId").GetString()}/access-token", new { }, auth)).GetProperty("token").GetString()!;

        // valid for the customer surface
        (await Raw(HttpMethod.Get, "/api/v1/customer/me", Bearer(token))).StatusCode.ShouldBe(HttpStatusCode.OK);
        // invalid token rejected
        (await Raw(HttpMethod.Get, "/api/v1/customer/me", Bearer("mt_forged"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        // a member token is NOT an API key: admin endpoints reject it
        (await Raw(HttpMethod.Get, "/api/v1/campaigns", Bearer(token))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Raw(HttpMethod.Post, "/api/v1/campaigns", Bearer(token), new { name = "x", triggerEventType = "PurchaseCompleted", effects = new[] { new { effectId = "x", type = "AwardBasePoints" } } })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---------- bootstrap ----------
    [Fact]
    public async Task Bootstrap_requires_the_configured_token()
    {
        (await Raw(HttpMethod.Post, "/api/v1/bootstrap", null, new { name = "Evil" })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Raw(HttpMethod.Post, "/api/v1/bootstrap", new() { ["X-Bootstrap-Token"] = "wrong" }, new { name = "Evil" })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---------- replay / idempotency ----------
    [Fact]
    public async Task Replaying_the_same_event_does_not_double_credit()
    {
        var (appId, apiKey) = await fx.OnboardChildAsync("sec-idem");
        var auth = Key(apiKey);
        await Configure(appId, auth);
        // an identical event (same payload) replayed under the same Idempotency-Key
        var evt = new { eventId = Guid.NewGuid(), eventType = "PurchaseCompleted", eventVersion = 1, sourceTransactionId = "IDEM1", customerId = "repeat", occurredAt = DateTimeOffset.UtcNow, correlationId = "IDEM1", attributes = new { orderId = "IDEM1", amount = 50000, currency = "IQD" } };
        var idem = new Dictionary<string, string> { ["X-Api-Key"] = apiKey, ["Idempotency-Key"] = "fixed-key" };

        var first = await fx.Post("/api/v1/events", evt, idem);
        await fx.PollRequest(first.GetProperty("requestId").GetString()!, auth);
        var second = await fx.Post("/api/v1/events", evt, idem); // same key + same body → replay
        second.GetProperty("requestId").GetString().ShouldBe(first.GetProperty("requestId").GetString()); // same request, not a new one
        second.GetProperty("duplicate").GetBoolean().ShouldBeTrue();
        await fx.PollRequest(second.GetProperty("requestId").GetString()!, auth);

        // only credited once (50,000 / 1,000 = 50)
        var m = await fx.Get("/api/v1/members/by-external-id/repeat", auth);
        m.GetProperty("wallet").GetProperty("activeBalance").GetInt64().ShouldBe(50);
    }

    // ---------- input limits ----------
    [Fact]
    public async Task Oversized_request_body_is_rejected()
    {
        var (_, apiKey) = await fx.OnboardChildAsync("sec-body");
        var huge = new string('A', 300 * 1024); // > 256 KB cap
        var resp = await Raw(HttpMethod.Post, "/api/v1/events", Key(apiKey), new
        {
            eventId = Guid.NewGuid(), eventType = "PurchaseCompleted", eventVersion = 1, sourceTransactionId = "big",
            customerId = "big", occurredAt = DateTimeOffset.UtcNow, correlationId = "big",
            attributes = new { orderId = "big", amount = 1000, currency = "IQD", note = huge },
        });
        // rejected with a client error — never accepted as a normal 202 (prod Kestrel returns 413;
        // the in-memory test host surfaces the over-limit body as a 4xx)
        resp.IsSuccessStatusCode.ShouldBeFalse();
        ((int)resp.StatusCode).ShouldBeInRange(400, 499);
    }

    [Fact]
    public async Task Event_burst_is_rate_limited()
    {
        var (appId, apiKey) = await fx.OnboardChildAsync("sec-rate");
        await Configure(appId, Key(apiKey));
        object Ev(int i) => new { eventId = Guid.NewGuid(), eventType = "PurchaseCompleted", eventVersion = 1, sourceTransactionId = $"rl{i}", customerId = "flood", occurredAt = DateTimeOffset.UtcNow, correlationId = $"rl{i}", attributes = new { orderId = $"rl{i}", amount = 1000, currency = "IQD" } };

        // fixture sets the limit to 20/sec; fire 60 concurrently on the same key → some are throttled
        var bursts = Enumerable.Range(0, 60).Select(i => Raw(HttpMethod.Post, "/api/v1/events", Key(apiKey), Ev(i)));
        var results = await Task.WhenAll(bursts);
        results.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests).ShouldBeGreaterThan(0);
    }

    // ---------- secret handling ----------
    [Fact]
    public async Task Account_listing_never_exposes_the_raw_key_or_hash()
    {
        var (appId, apiKey) = await fx.OnboardChildAsync("sec-secret");
        var created = await fx.Post($"/api/v1/applications/{appId}/accounts", new { accountName = "svc" }, Key(fx.RootKey));
        created.GetProperty("apiKey").GetString().ShouldStartWith("lk_live_"); // returned exactly once, at creation

        var raw = await (await Raw(HttpMethod.Get, $"/api/v1/applications/{appId}/accounts", Key(apiKey))).Content.ReadAsStringAsync();
        raw.ShouldContain("keyPrefix");
        raw.ShouldNotContain("keyHash");
        raw.ShouldNotContain("apiKey");
        raw.ShouldNotContain(created.GetProperty("apiKey").GetString()!); // the full secret is never listed
    }
}
