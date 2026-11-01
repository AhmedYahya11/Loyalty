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
        ["/openapi", "/scalar", "/health", "/api/v1/bootstrap", "/api/v1/admin/login",
         "/api/v1/connectors/shopify/", // inbound webhooks authenticate by HMAC, not API key
         "/"];

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

        // dashboard operator: a validated Identity JWT (populated by UseAuthentication) authenticates + applies RBAC
        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            await InvokeAdminAsync(ctx);
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

    /// <summary>Establish scope + role from a validated Identity JWT and apply RBAC (P17). Signature +
    /// expiry were already checked by JwtBearer (UseAuthentication); here we add the revocation/active
    /// check (jti → session) and the read-only guard, then set the ambient contexts.</summary>
    private async Task InvokeAdminAsync(HttpContext ctx)
    {
        var sp = ctx.RequestServices;
        var identity = sp.GetRequiredService<Loyalty.Core.Modules.Identity.Application.AdminIdentityService>();
        var resolver = sp.GetRequiredService<ApplicationScopeResolver>();
        var scope = sp.GetRequiredService<ApplicationContext>();
        var admin = sp.GetRequiredService<AdminContext>();

        var u = ctx.User;
        var username = u.FindFirst(JwtAuth.NameClaim)?.Value ?? u.Identity?.Name ?? "";
        var role = Enum.TryParse<Loyalty.Core.Modules.Identity.Domain.AdminRole>(u.FindFirst(JwtAuth.RoleClaim)?.Value, true, out var r)
            ? r : Loyalty.Core.Modules.Identity.Domain.AdminRole.Viewer;
        if (!Guid.TryParse(u.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value, out var userId)
            || !Guid.TryParse(u.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value, out var sessionId)
            || !Guid.TryParse(u.FindFirst(JwtAuth.AppIdClaim)?.Value, out var appId))
        { await Write(ctx, LoyaltyError.InvalidCredential()); return; }

        // revocation + account-active check on top of the stateless signature validation
        if (!await identity.IsSessionActiveAsync(sessionId, userId, ctx.RequestAborted))
        { await Write(ctx, LoyaltyError.InvalidCredential()); return; }
        if (!await resolver.IsActiveChainAsync(appId, ctx.RequestAborted))
        { await Write(ctx, LoyaltyError.CredentialDisabled()); return; }

        // Viewers are read-only: block any mutating request
        if (role == Loyalty.Core.Modules.Identity.Domain.AdminRole.Viewer
            && !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method) && !HttpMethods.IsOptions(ctx.Request.Method))
        { await Write(ctx, LoyaltyError.Forbidden("Your role is read-only.")); return; }

        var visible = await resolver.ResolveVisibleAsync(appId, ctx.RequestAborted);
        scope.Establish(appId, Guid.Empty, $"admin:{username}", visible);
        admin.Establish(userId, username, appId, role, sessionId);

        using (Serilog.Context.LogContext.PushProperty("ApplicationId", appId))
        using (Serilog.Context.LogContext.PushProperty("AdminUser", username))
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
