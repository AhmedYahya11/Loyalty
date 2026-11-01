using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using JasperFx.Resources;
using Loyalty.Api;
using Loyalty.Api.Auth;
using Loyalty.Core;
using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
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

// JSON shape for minimal-API results (health checks etc.)
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// unified audit trail (B5): record every successful mutating admin action
builder.Services.AddScoped<AuditActionFilter>();

// MVC controllers (grouped by resource) — mirror the same JSON shape as above.
builder.Services.AddControllers(o => o.Filters.AddService<AuditActionFilter>()).AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// Identity-issued JWT bearer auth for the dashboard + policy-based authorization.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false; // keep raw claim names (sub, name, role, app_id, jti)
        o.TokenValidationParameters = JwtAuth.ValidationParameters();
    });
builder.Services.AddAuthorization(o =>
    o.AddPolicy("ManageUsers", p => p.RequireRole("Admin", "SuperAdmin")));

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

    // seed the first SuperAdmin against the root application (P17) — no-op if any admin user exists
    var rootApp = await db.Applications.Where(a => a.ParentId == null).OrderBy(a => a.CreatedAt).FirstOrDefaultAsync();
    if (rootApp is not null)
    {
        var identity = scope.ServiceProvider.GetRequiredService<Loyalty.Core.Modules.Identity.Application.AdminIdentityService>();
        var suUser = Environment.GetEnvironmentVariable("SUPERADMIN_USERNAME") ?? "superadmin";
        var suPass = Environment.GetEnvironmentVariable("SUPERADMIN_PASSWORD") ?? "ChangeMe!123";
        await identity.EnsureSuperAdminAsync(rootApp.Id, suUser, suPass, CancellationToken.None);
    }
}

app.MapOpenApi();
app.MapScalarApiReference(o => o.WithTitle("KĪNŪTU API — كينوتو"));

app.UseCors(); // must precede the auth middleware so preflight (OPTIONS) is answered without a key
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseAuthentication();                        // validate the Identity JWT (populates ctx.User)
app.UseMiddleware<ApiKeyAuthMiddleware>();      // establish tenant/admin/customer scope
app.UseAuthorization();                         // enforce [Authorize] / policies on controllers
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithTags("Health");   // liveness
app.MapHealthChecks("/health/ready");                                    // readiness (Postgres)
app.MapControllers();

app.Run();

// exposed for Alba / WebApplicationFactory integration tests
public partial class Program;
