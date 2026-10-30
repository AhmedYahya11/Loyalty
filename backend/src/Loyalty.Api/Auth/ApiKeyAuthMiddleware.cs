using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Accounts.Application;
using Loyalty.Core.Modules.Accounts.Domain;
using Loyalty.Core.Modules.Applications.Application;

namespace Loyalty.Api.Auth;

/// <summary>
/// Authenticates the X-Api-Key, validates the active chain, resolves the target application
/// (own or a visible descendant via X-Application-Id), and establishes the application scope
/// before any endpoint runs (MVP spec §5, §7, PROCESSING_FLOW diagram 1/2).
/// </summary>
public sealed class ApiKeyAuthMiddleware(RequestDelegate next)
{
    private static readonly string[] AnonymousPrefixes =
        ["/openapi", "/scalar", "/health", "/api/v1/bootstrap", "/"];

    public async Task InvokeAsync(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? "/";
        if (IsAnonymous(path)) { await next(ctx); return; }

        var sp = ctx.RequestServices;
        var accounts = sp.GetRequiredService<AccountService>();
        var resolver = sp.GetRequiredService<ApplicationScopeResolver>();
        var scope = sp.GetRequiredService<ApplicationContext>();

        var apiKey = ctx.Request.Headers["X-Api-Key"].ToString();
        var account = await accounts.AuthenticateAsync(apiKey, ctx.RequestAborted);
        if (account is null) { await Write(ctx, LoyaltyError.InvalidCredential()); return; }

        if (!await resolver.IsActiveChainAsync(account.ApplicationId, ctx.RequestAborted))
        { await Write(ctx, LoyaltyError.CredentialDisabled()); return; }

        var targetApplicationId = account.ApplicationId;
        var header = ctx.Request.Headers["X-Application-Id"].ToString();
        if (!string.IsNullOrWhiteSpace(header))
        {
            if (!Guid.TryParse(header, out var requested))
            { await Write(ctx, LoyaltyError.ValidationFailed("X-Application-Id must be a GUID.")); return; }

            if (requested != account.ApplicationId)
            {
                var visible = await resolver.IsVisibleAsync(account.ApplicationId, requested, ctx.RequestAborted);
                if (!visible) { await Write(ctx, LoyaltyError.ApplicationOutsideSubtree()); return; }
                if (!await resolver.IsActiveChainAsync(requested, ctx.RequestAborted))
                { await Write(ctx, LoyaltyError.CredentialDisabled()); return; }
            }
            targetApplicationId = requested;
        }

        var visibleIds = await resolver.ResolveVisibleAsync(targetApplicationId, ctx.RequestAborted);
        scope.Establish(targetApplicationId, account.Id, account.AccountName, visibleIds);
        ctx.Items[nameof(Account)] = account;

        // enrich logs/traces with scope identity (never the raw key) — §17
        using (Serilog.Context.LogContext.PushProperty("ApplicationId", targetApplicationId))
        using (Serilog.Context.LogContext.PushProperty("AccountId", account.Id))
        using (Serilog.Context.LogContext.PushProperty("CorrelationId", ctx.Request.Headers["X-Correlation-Id"].ToString()))
        {
            await next(ctx);
        }
    }

    private static bool IsAnonymous(string path) =>
        path == "/" || AnonymousPrefixes.Any(p => p != "/" && path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static Task Write(HttpContext ctx, LoyaltyError error)
    {
        ctx.Response.StatusCode = error.StatusCode;
        ctx.Response.ContentType = "application/json";
        var message = error.Localized(RequestLanguage.IsArabic(ctx));
        return ctx.Response.WriteAsJsonAsync(new { code = error.Code, message });
    }
}
