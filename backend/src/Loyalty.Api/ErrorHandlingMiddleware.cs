using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Api;

/// <summary>Maps <see cref="LoyaltyException"/> to a localized JSON error (EN / AR / KU).</summary>
public sealed class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (LoyaltyException ex)
        {
            await WriteAsync(ctx, ex.Error.StatusCode, ex.Error.Code, ex.Error.Localized(RequestLanguage.Lang(ctx)));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception");
            var message = RequestLanguage.Lang(ctx) switch
            {
                "ar" => "حدث خطأ غير متوقع.",
                "ku" => "هەڵەیەکی چاوەڕواننەکراو ڕوویدا.",
                _ => "An unexpected error occurred.",
            };
            await WriteAsync(ctx, 500, "internal_error", message);
        }
    }

    private static Task WriteAsync(HttpContext ctx, int status, string code, string message)
    {
        if (ctx.Response.HasStarted) return Task.CompletedTask;
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        return ctx.Response.WriteAsJsonAsync(new { code, message });
    }
}
