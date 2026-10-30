using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Shouldly;

namespace Loyalty.Tests.Integration;

/// <summary>
/// Phase-4 gate: result events land in the notifications feed, analytics overview aggregates
/// correctly, and a webhook subscription receives a signed delivery.
/// </summary>
[Collection("api")]
public class P4E2ETests(ApiFixture fx)
{
    private async Task<(string appId, Dictionary<string, string> auth, string memberId)> SetupEarningApp(string name)
    {
        var (appId, apiKey) = await fx.OnboardChildAsync(name);
        var auth = ApiFixture.Auth(apiKey);
        await fx.Put($"/api/v1/applications/{appId}/loyalty-settings", new
        {
            pointsUnitCode = "POINT", baseCurrency = "IQD", spendAmountPerPoint = 1000,
            roundingPolicy = "Floor", loyaltyStatus = "Active",
        }, auth);
        var member = await fx.Post("/api/v1/members", new { externalCustomerId = "c1" }, auth);
        var c = await fx.Post("/api/v1/campaigns", new
        {
            name = "Base Earn", triggerEventType = "PurchaseCompleted", minimumEventVersion = 1,
            priority = 100, stackingPolicy = "Stackable", conditions = Array.Empty<object>(),
            effects = new[] { new { effectId = "base-earn", type = "AwardBasePoints" } },
        }, auth);
        await fx.ActivateCampaign(c.GetProperty("id").GetString()!, auth);
        return (appId, auth, member.GetProperty("memberId").GetString()!);
    }

    private static Dictionary<string, string> Sub(Dictionary<string, string> auth, string k) =>
        new() { ["X-Api-Key"] = auth["X-Api-Key"], ["Idempotency-Key"] = k };

    private static object Purchase(string orderId, int amount) => new
    {
        eventId = Guid.NewGuid(), eventType = "PurchaseCompleted", eventVersion = 1,
        sourceTransactionId = orderId, customerId = "c1", occurredAt = DateTimeOffset.UtcNow,
        correlationId = orderId, attributes = new { orderId, amount, currency = "IQD" },
    };

    [Fact]
    public async Task Result_event_appears_in_notifications_and_analytics()
    {
        var (_, auth, _) = await SetupEarningApp("p4-feed");

        var r = await fx.Post("/api/v1/events", Purchase("ORDER-1", 50000), Sub(auth, "p4-1"));
        (await fx.PollRequest(r.GetProperty("requestId").GetString()!, auth)).GetProperty("status").GetString().ShouldBe("Completed");

        var feed = await fx.Get("/api/v1/notifications", auth);
        var items = feed.GetProperty("items").EnumerateArray().ToList();
        var awarded = items.FirstOrDefault(i => i.GetProperty("eventType").GetString() == "PointsAwardedV1");
        awarded.ValueKind.ShouldNotBe(System.Text.Json.JsonValueKind.Undefined);
        awarded.GetProperty("payload").GetProperty("awardedPoints").GetInt64().ShouldBe(50);

        var overview = await fx.Get("/api/v1/analytics/overview", auth);
        overview.GetProperty("members").GetInt32().ShouldBe(1);
        overview.GetProperty("activeCampaigns").GetInt32().ShouldBe(1);
        overview.GetProperty("pointsIssued").GetInt64().ShouldBe(50);
        overview.GetProperty("lifecycle").GetProperty("New").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Webhook_receives_signed_delivery()
    {
        var (_, auth, _) = await SetupEarningApp("p4-webhook");

        var port = FreePort();
        var hookUrl = $"http://localhost:{port}/hook/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(hookUrl);
        listener.Start();
        var received = new TaskCompletionSource<(string body, string? evt, string? sig)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(async () =>
        {
            try
            {
                var ctx = await listener.GetContextAsync();
                using var reader = new StreamReader(ctx.Request.InputStream);
                var body = await reader.ReadToEndAsync();
                var evt = ctx.Request.Headers["X-Loyalty-Event"];
                var sig = ctx.Request.Headers["X-Loyalty-Signature"];
                ctx.Response.StatusCode = 200;
                ctx.Response.Close();
                received.TrySetResult((body, evt, sig));
            }
            catch { /* listener stopped */ }
        });

        var sub = await fx.Post("/api/v1/webhooks", new { url = hookUrl, eventTypes = new[] { "PointsAwardedV1" } }, auth);
        var secret = sub.GetProperty("secret").GetString()!;

        var r = await fx.Post("/api/v1/events", Purchase("ORDER-2", 50000), Sub(auth, "p4-2"));
        await fx.PollRequest(r.GetProperty("requestId").GetString()!, auth);

        await fx.Post("/api/v1/webhooks/dispatch", new { }, auth);

        var done = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(15)));
        done.ShouldBe(received.Task, "webhook was not delivered in time");
        var (body, evt, sig) = await received.Task;

        evt.ShouldBe("PointsAwardedV1");
        var expected = "sha256=" + Convert.ToHexStringLower(
            new HMACSHA256(Encoding.UTF8.GetBytes(secret)).ComputeHash(Encoding.UTF8.GetBytes(body)));
        sig.ShouldBe(expected);
        body.ShouldContain("awardedPoints");

        listener.Stop();
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
