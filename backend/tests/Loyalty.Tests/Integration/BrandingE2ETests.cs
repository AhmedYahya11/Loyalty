using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace Loyalty.Tests.Integration;

/// <summary>
/// B6 — per-tenant white-labeling. An admin sets a tenant's branding (name/logo/colors); the member's
/// customer app reads it (member-token scope); colors are validated hex; and a member only ever sees
/// their own tenant's branding (isolation).
/// </summary>
[Collection("api")]
public class BrandingE2ETests(ApiFixture fx)
{
    private static Dictionary<string, string> Bearer(string t) => new() { ["Authorization"] = $"Bearer {t}" };

    private async Task<HttpResponseMessage> Raw(HttpMethod m, string url, Dictionary<string, string> headers, object? body = null)
    {
        var req = new HttpRequestMessage(m, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        foreach (var (k, v) in headers) req.Headers.TryAddWithoutValidation(k, v);
        return await fx.Client.SendAsync(req);
    }

    [Fact]
    public async Task Admin_sets_branding_and_the_customer_app_reads_its_own_tenants_brand()
    {
        var (appId, apiKey) = await fx.OnboardChildAsync("brand");
        var auth = ApiFixture.Auth(apiKey);
        await fx.Put($"/api/v1/applications/{appId}/loyalty-settings", new
        {
            pointsUnitCode = "POINT", baseCurrency = "IQD", spendAmountPerPoint = 1000,
            roundingPolicy = "Floor", loyaltyStatus = "Active",
        }, auth);

        // invalid color → 422
        (await Raw(HttpMethod.Put, $"/api/v1/applications/{appId}/branding", auth,
            new { brandName = "Hareer", primaryColor = "notacolor" })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // admin sets branding
        var put = await fx.Put($"/api/v1/applications/{appId}/branding", new
        {
            brandName = "Hareer Rewards", logoUrl = "https://cdn.example.com/logo.png",
            primaryColor = "#173B59", accentColor = "#B18A43",
        }, auth);
        put.GetProperty("brandName").GetString().ShouldBe("Hareer Rewards");

        // a member of this tenant reads its branding via the customer surface
        var member = await fx.Post("/api/v1/members", new { externalCustomerId = "brand-cust" }, auth);
        var token = (await fx.Post($"/api/v1/members/{member.GetProperty("memberId").GetString()}/access-token", new { }, auth)).GetProperty("token").GetString()!;
        var brand = await fx.Get("/api/v1/customer/branding", Bearer(token));
        brand.GetProperty("brandName").GetString().ShouldBe("Hareer Rewards");
        brand.GetProperty("primaryColor").GetString().ShouldBe("#173B59");
        brand.GetProperty("accentColor").GetString().ShouldBe("#B18A43");

        // a member of a DIFFERENT tenant (no branding set) gets null — no cross-tenant leakage
        var (_, apiKey2) = await fx.OnboardChildAsync("brand2");
        var auth2 = ApiFixture.Auth(apiKey2);
        await fx.Put($"/api/v1/applications/{(await fx.Get("/api/v1/me", auth2)).GetProperty("applicationId").GetString()}/loyalty-settings", new
        {
            pointsUnitCode = "POINT", baseCurrency = "IQD", spendAmountPerPoint = 1000,
            roundingPolicy = "Floor", loyaltyStatus = "Active",
        }, auth2);
        var member2 = await fx.Post("/api/v1/members", new { externalCustomerId = "brand-cust-2" }, auth2);
        var token2 = (await fx.Post($"/api/v1/members/{member2.GetProperty("memberId").GetString()}/access-token", new { }, auth2)).GetProperty("token").GetString()!;
        var brand2 = await fx.Get("/api/v1/customer/branding", Bearer(token2));
        brand2.ValueKind.ShouldBeOneOf(System.Text.Json.JsonValueKind.Null, System.Text.Json.JsonValueKind.Undefined); // no branding, and no cross-tenant leak
    }
}
