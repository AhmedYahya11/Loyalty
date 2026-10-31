using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Accounts.Application;
using Loyalty.Core.Modules.Accounts.Domain;
using Loyalty.Core.Modules.Applications.Application;
using Loyalty.Core.Modules.Members.Application;

namespace Loyalty.Api.Auth;

/// <summary>
/// Authenticates the X-Api-Key, validates the active chain, resolves the target application
/// (own or a visible descendant via X-Application-Id), and establishes the application scope
/// before any endpoint runs (MVP spec §5, §7, PROCESSING_FLOW diagram 1/2).
/// </summary>
public sealed class ApiKeyAuthMiddleware(RequestDelegate next)
{
    private static readonly string[] AnonymousPrefixes =
        ["/openapi", "/scalar", "/health", "/api/v1/bootstrap", "/api/v1/admin/login", "/"];

    public async Task InvokeAsync(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? "/";
        if (IsAnonymous(path)) { await next(ctx); return; }

        // customer-facing app: authenticate a member access token (Bearer) and scope to that member.
        // Trailing slash is required so the admin "/api/v1/customers" list is NOT caught here.
        if (path.StartsWith("/api/v1/customer/", StringComparison.OrdinalIgnoreCase))
        {
            await InvokeCustomerAsync(ctx);
            return;
        }

        // dashboard operator: an admin-session Bearer token (as_…) authenticates + applies RBAC
        var authHeader = ctx.Request.Headers.Authorization.ToString();
        if (authHeader.Contains("as_", StringComparison.Ordinal))
        {
            await InvokeAdminAsync(ctx, authHeader);
            return;
        }

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

    /// <summary>Authenticate a member access token and establish member + application scope (P11).</summary>
    private async Task InvokeCustomerAsync(HttpContext ctx)
    {
        var sp = ctx.RequestServices;
        var tokens = sp.GetRequiredService<MemberTokenService>();
        var resolver = sp.GetRequiredService<ApplicationScopeResolver>();
        var scope = sp.GetRequiredService<ApplicationContext>();
        var customer = sp.GetRequiredService<CustomerContext>();

        var auth = ctx.Request.Headers.Authorization.ToString();
        var token = auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? auth["Bearer ".Length..].Trim() : auth.Trim();

        var resolved = await tokens.ResolveAsync(token, ctx.RequestAborted);
        if (resolved is null) { await Write(ctx, LoyaltyError.InvalidCredential()); return; }

        if (!await resolver.IsActiveChainAsync(resolved.ApplicationId, ctx.RequestAborted))
        { await Write(ctx, LoyaltyError.CredentialDisabled()); return; }

        // a customer sees only their own application (self), never descendants
        scope.Establish(resolved.ApplicationId, Guid.Empty, "customer-app", [resolved.ApplicationId]);
        customer.Establish(resolved.ApplicationId, resolved.MemberId);

        using (Serilog.Context.LogContext.PushProperty("ApplicationId", resolved.ApplicationId))
        using (Serilog.Context.LogContext.PushProperty("MemberId", resolved.MemberId))
        {
            await next(ctx);
        }
    }

    /// <summary>Authenticate a dashboard admin session (Bearer as_…), establish scope + role, apply RBAC (P17).</summary>
    private async Task InvokeAdminAsync(HttpContext ctx, string authHeader)
    {
        var sp = ctx.RequestServices;
        var identity = sp.GetRequiredService<Loyalty.Core.Modules.Identity.Application.AdminIdentityService>();
        var resolver = sp.GetRequiredService<ApplicationScopeResolver>();
        var scope = sp.GetRequiredService<ApplicationContext>();
        var admin = sp.GetRequiredService<AdminContext>();

        var token = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authHeader["Bearer ".Length..].Trim() : authHeader.Trim();
        var resolved = await identity.ResolveAsync(token, ctx.RequestAborted);
        if (resolved is null) { await Write(ctx, LoyaltyError.InvalidCredential()); return; }
        if (!await resolver.IsActiveChainAsync(resolved.ApplicationId, ctx.RequestAborted))
        { await Write(ctx, LoyaltyError.CredentialDisabled()); return; }

        // Viewers are read-only: block any mutating request (login/logout are handled elsewhere)
        if (resolved.Role == Loyalty.Core.Modules.Identity.Domain.AdminRole.Viewer
            && !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method) && !HttpMethods.IsOptions(ctx.Request.Method))
        { await Write(ctx, LoyaltyError.Forbidden("Your role is read-only.")); return; }

        var visible = await resolver.ResolveVisibleAsync(resolved.ApplicationId, ctx.RequestAborted);
        scope.Establish(resolved.ApplicationId, Guid.Empty, $"admin:{resolved.Username}", visible);
        admin.Establish(resolved.UserId, resolved.Username, resolved.ApplicationId, resolved.Role);

        using (Serilog.Context.LogContext.PushProperty("ApplicationId", resolved.ApplicationId))
        using (Serilog.Context.LogContext.PushProperty("AdminUser", resolved.Username))
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
        var message = error.Localized(RequestLanguage.Lang(ctx));
        return ctx.Response.WriteAsJsonAsync(new { code = error.Code, message });
    }
}
