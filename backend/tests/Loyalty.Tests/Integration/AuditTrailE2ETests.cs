using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace Loyalty.Tests.Integration;

/// <summary>
/// B5 — unified admin audit trail. Every successful mutating admin action is recorded (who/what/when);
/// the trail is readable by Admin/SuperAdmin (ManageUsers policy) and denied to lower roles.
/// </summary>
[Collection("api")]
public class AuditTrailE2ETests(ApiFixture fx)
{
    private static Dictionary<string, string> Bearer(string t) => new() { ["Authorization"] = $"Bearer {t}" };

    private async Task<HttpResponseMessage> Raw(HttpMethod m, string url, string bearer)
    {
        var req = new HttpRequestMessage(m, url);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearer}");
        return await fx.Client.SendAsync(req);
    }

    [Fact]
    public async Task Successful_admin_mutations_are_recorded_and_readable_by_admins_only()
    {
        var token = (await fx.Post("/api/v1/admin/login", new { username = "superadmin", password = "ChangeMe!123" })).GetProperty("token").GetString()!;
        var auth = Bearer(token);

        // a mutating admin action (create a tier)
        await fx.Post("/api/v1/tiers", new
        {
            name = "Audit-" + Guid.NewGuid().ToString("N")[..6], rank = 7,
            thresholdMetric = "NetSpend", thresholdValue = 1, earnMultiplier = 1,
        }, auth);

        // the audit trail records it (who/what/when)
        var trail = await fx.Get("/api/v1/admin/audit", auth);
        var items = trail.GetProperty("items").EnumerateArray().ToList();
        items.ShouldContain(e =>
            e.GetProperty("method").GetString() == "POST"
            && e.GetProperty("path").GetString() == "/api/v1/tiers"
            && e.GetProperty("username").GetString() == "superadmin");

        // a GET (non-mutating) is NOT recorded as an action
        items.ShouldNotContain(e => e.GetProperty("method").GetString() == "GET");

        // a Viewer cannot read the audit trail (ManageUsers policy → 403)
        var viewerName = "aud-viewer-" + Guid.NewGuid().ToString("N")[..8];
        await fx.Post("/api/v1/admin/users", new { username = viewerName, password = "viewerpass1", role = "Viewer" }, auth);
        var vToken = (await fx.Post("/api/v1/admin/login", new { username = viewerName, password = "viewerpass1" })).GetProperty("token").GetString()!;
        (await Raw(HttpMethod.Get, "/api/v1/admin/audit", vToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
