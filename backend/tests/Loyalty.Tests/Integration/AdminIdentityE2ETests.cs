using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;

namespace Loyalty.Tests.Integration;

/// <summary>
/// Phase-17 admin identity + RBAC: the bootstrap seeds a SuperAdmin; username/password login issues a
/// session; roles gate access (Viewers are read-only, only Admin/SuperAdmin manage users); and the
/// Customers list is reachable. Bad credentials are rejected.
/// </summary>
[Collection("api")]
public class AdminIdentityE2ETests(ApiFixture fx)
{
    private static Dictionary<string, string> Bearer(string t) => new() { ["Authorization"] = $"Bearer {t}" };

    private async Task<HttpResponseMessage> Raw(HttpMethod m, string url, string? bearer = null, object? body = null)
    {
        var req = new HttpRequestMessage(m, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        if (bearer is not null) req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearer}");
        return await fx.Client.SendAsync(req);
    }

    [Fact]
    public async Task Superadmin_login_rbac_and_customers()
    {
        // bad credentials → 401
        (await Raw(HttpMethod.Post, "/api/v1/admin/login", body: new { username = "superadmin", password = "wrong" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // the seeded SuperAdmin logs in
        var login = await fx.Post("/api/v1/admin/login", new { username = "superadmin", password = "ChangeMe!123" });
        var token = login.GetProperty("token").GetString()!;
        token.ShouldStartWith("as_");
        login.GetProperty("role").GetString().ShouldBe("SuperAdmin");

        // /admin/me reflects the session + management capability
        var me = await fx.Get("/api/v1/admin/me", Bearer(token));
        me.GetProperty("role").GetString().ShouldBe("SuperAdmin");
        me.GetProperty("canManageUsers").GetBoolean().ShouldBeTrue();

        // SuperAdmin creates a read-only Viewer
        var viewerName = "viewer-" + Guid.NewGuid().ToString("N")[..8];
        var created = await fx.Post("/api/v1/admin/users", new { username = viewerName, password = "viewerpass1", role = "Viewer" }, Bearer(token));
        created.GetProperty("role").GetString().ShouldBe("Viewer");

        // Viewer logs in
        var vToken = (await fx.Post("/api/v1/admin/login", new { username = viewerName, password = "viewerpass1" })).GetProperty("token").GetString()!;
        (await fx.Get("/api/v1/admin/me", Bearer(vToken))).GetProperty("role").GetString().ShouldBe("Viewer");

        // Viewer can READ customers but cannot WRITE (read-only) and cannot manage users
        (await Raw(HttpMethod.Get, "/api/v1/customers", vToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Raw(HttpMethod.Post, "/api/v1/tiers", vToken, new { name = "X", rank = 9, thresholdMetric = "NetSpend", thresholdValue = 1, earnMultiplier = 1 }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Raw(HttpMethod.Get, "/api/v1/admin/users", vToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // customers list is reachable for the SuperAdmin and returns a JSON items array
        var customers = await fx.Get("/api/v1/customers", Bearer(token));
        customers.GetProperty("items").ValueKind.ShouldBe(JsonValueKind.Array);

        // a member token (mt_) must NOT authenticate the admin surface
        (await Raw(HttpMethod.Get, "/api/v1/admin/me", "mt_not_an_admin")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
