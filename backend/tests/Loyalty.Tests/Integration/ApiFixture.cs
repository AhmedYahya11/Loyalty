using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace Loyalty.Tests.Integration;

/// <summary>
/// Boots the real API (Program), Wolverine durable messaging, and PostgreSQL via
/// Testcontainers — no mocks — so integration tests exercise the true pipeline (§15, §19).
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = default!;
    public HttpClient Client { get; private set; } = default!;

    // Matches appsettings.json so the bootstrap endpoint accepts it in the test host.
    public const string BootstrapToken = "dev-bootstrap-token-change-me";

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();

        // Program reads LOYALTY_DB first; set it before the host boots.
        Environment.SetEnvironmentVariable("LOYALTY_DB", _pg.GetConnectionString());

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development"));
        Client = Factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable("LOYALTY_DB", null);
        Factory.Dispose();
        await _pg.DisposeAsync();
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;
