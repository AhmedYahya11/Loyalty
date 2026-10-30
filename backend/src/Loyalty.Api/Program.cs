using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using JasperFx.Resources;
using Loyalty.Api;
using Loyalty.Api.Auth;
using Loyalty.Core;
using Loyalty.Core.Persistence;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Serilog;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration).Enrich.FromLogContext().WriteTo.Console());

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

// cap request bodies (loyalty events are small); configurable.
var maxBody = builder.Configuration.GetValue<long?>("Limits:MaxRequestBodyBytes") ?? 256 * 1024;
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = maxBody);

// per-API-key rate limiting on event ingestion (§17).
var eventsPerSecond = builder.Configuration.GetValue<int?>("RateLimit:EventsPerSecond") ?? 100;
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.ContentType = "application/json";
        await ctx.HttpContext.Response.WriteAsJsonAsync(new { code = "rate_limited", message = "Too many requests." }, ct);
    };
    o.AddPolicy("events", ctx => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ctx.Request.Headers["X-Api-Key"].ToString(),
        factory: _ => new FixedWindowRateLimiterOptions { PermitLimit = eventsPerSecond, Window = TimeSpan.FromSeconds(1), QueueLimit = 0 }));
});

// CORS so the browser dashboard (a different origin) can call the API. Auth is header-based
// (X-Api-Key), not cookies, so any-origin is safe for dev; lock down Cors:Origins in prod.
var corsOrigins = builder.Configuration.GetValue<string>("Cors:Origins");
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (string.IsNullOrWhiteSpace(corsOrigins) || corsOrigins == "*")
        p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    else
        p.WithOrigins(corsOrigins.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .AllowAnyHeader().AllowAnyMethod();
}));

// health checks
builder.Services.AddHealthChecks().AddDbContextCheck<LoyaltyDbContext>("postgres");

// OpenTelemetry traces + metrics (§17). OTLP export is enabled when an endpoint is configured.
var otel = builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("Loyalty.Api"))
    .WithTracing(t => t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
    .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation());
if (Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") is not null)
{
    otel.WithTracing(t => t.AddOtlpExporter());
    otel.WithMetrics(m => m.AddOtlpExporter());
}

var app = builder.Build();

// schema bootstrap: apply EF migrations + the balancing trigger.
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

app.UseCors(); // must precede the auth middleware so preflight (OPTIONS) is answered without a key
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<ApiKeyAuthMiddleware>();
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));         // liveness
app.MapHealthChecks("/health/ready");                                    // readiness (Postgres)
app.MapLoyaltyEndpoints();

app.Run();

// exposed for Alba / WebApplicationFactory integration tests
public partial class Program;
