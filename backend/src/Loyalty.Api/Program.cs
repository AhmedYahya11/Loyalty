using System.Text.Json;
using System.Text.Json.Serialization;
using JasperFx.Resources;
using Loyalty.Api;
using Loyalty.Api.Auth;
using Loyalty.Core;
using Loyalty.Core.Persistence;
using Scalar.AspNetCore;
using Serilog;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

var conn = Environment.GetEnvironmentVariable("LOYALTY_DB")
    ?? builder.Configuration.GetConnectionString("Loyalty")
    ?? "Host=localhost;Port=5432;Database=loyalty;Username=loyalty;Password=loyalty";

builder.Services.AddLoyaltyCore(conn);
builder.Host.UseWolverine(o => LoyaltyWolverine.Configure(o, conn));
builder.Services.AddResourceSetupOnStartup();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddOpenApi();

var app = builder.Build();

// Phase 1 schema bootstrap (EnsureCreated + balancing trigger); production will use migrations.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LoyaltyDbContext>();
    await DbBootstrap.InitializeAsync(db);

    // ensure Wolverine's durable message-store tables exist
    var store = scope.ServiceProvider.GetService<Wolverine.Persistence.Durability.IMessageStore>();
    if (store is not null) await store.Admin.MigrateAsync();
}

app.MapOpenApi();
app.MapScalarApiReference(o => o.WithTitle("Loyalty Platform API — منصة الولاء"));

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<ApiKeyAuthMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapLoyaltyEndpoints();

app.Run();

// exposed for Alba / WebApplicationFactory integration tests
public partial class Program;
