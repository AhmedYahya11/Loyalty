using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace Loyalty.Tests.Integration;

/// <summary>
/// Boots the real API (Program), Wolverine durable messaging, and PostgreSQL via
/// Testcontainers — no mocks — so integration tests exercise the true pipeline (§15, §19).
/// Bootstraps the root once and hands each test an isolated child application.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = default!;
    public HttpClient Client { get; private set; } = default!;
    public string RootKey { get; private set; } = default!;
    public string RootAppId { get; private set; } = default!;
    public const string BootstrapToken = "dev-bootstrap-token-change-me";

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        Environment.SetEnvironmentVariable("LOYALTY_DB", _pg.GetConnectionString());
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development"));
        Client = Factory.CreateClient();

        var boot = await Post("/api/v1/bootstrap", new { name = "Hareer", nameAr = "حرير" },
            new() { ["X-Bootstrap-Token"] = BootstrapToken });
        RootAppId = boot.GetProperty("application").GetProperty("id").GetString()!;
        RootKey = boot.GetProperty("rootAccount").GetProperty("apiKey").GetString()!;
    }

    public async Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable("LOYALTY_DB", null);
        Factory.Dispose();
        await _pg.DisposeAsync();
    }

    /// <summary>Create an isolated child application + its own API key for one test.</summary>
    public async Task<(string appId, string apiKey)> OnboardChildAsync(string name)
    {
        var root = Auth(RootKey);
        var app = await Post("/api/v1/applications", new { name, parentId = RootAppId }, root);
        var appId = app.GetProperty("id").GetString()!;
        var acct = await Post($"/api/v1/applications/{appId}/accounts", new { accountName = $"{name}-api" }, root);
        return (appId, acct.GetProperty("apiKey").GetString()!);
    }

    public static Dictionary<string, string> Auth(string key) => new() { ["X-Api-Key"] = key };

    public Task<JsonElement> Post(string url, object body, Dictionary<string, string>? headers = null) =>
        Send(new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) }, headers);
    public Task<JsonElement> Put(string url, object body, Dictionary<string, string>? headers = null) =>
        Send(new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) }, headers);
    public Task<JsonElement> Get(string url, Dictionary<string, string>? headers = null) =>
        Send(new HttpRequestMessage(HttpMethod.Get, url), headers);

    private async Task<JsonElement> Send(HttpRequestMessage req, Dictionary<string, string>? headers)
    {
        if (headers is not null) foreach (var (k, v) in headers) req.Headers.TryAddWithoutValidation(k, v);
        var resp = await Client.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{(int)resp.StatusCode} {req.Method} {req.RequestUri}: {text}");
        return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>Poll a request until terminal.</summary>
    public async Task<JsonElement> PollRequest(string requestId, Dictionary<string, string> auth)
    {
        for (var i = 0; i < 40; i++)
        {
            var r = await Get($"/api/v1/requests/{requestId}", auth);
            var s = r.GetProperty("status").GetString();
            if (s is "Completed" or "Rejected" or "Failed") return r;
            await Task.Delay(500);
        }
        throw new TimeoutException($"Request {requestId} did not reach a terminal status.");
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;
