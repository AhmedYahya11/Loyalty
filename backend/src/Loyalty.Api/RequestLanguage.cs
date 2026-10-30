namespace Loyalty.Api;

/// <summary>Resolves the response language from the Accept-Language header (en / ar / ku).</summary>
public static class RequestLanguage
{
    public static bool IsArabic(HttpContext ctx) => Lang(ctx) == "ar";

    /// <summary>Normalized language code from Accept-Language: "ar", "ku", or "en" (default).
    /// Kurdish (Sorani) uses the Arabic script and is right-to-left.</summary>
    public static string Lang(HttpContext ctx)
    {
        var header = ctx.Request.Headers.AcceptLanguage.ToString().TrimStart();
        if (header.StartsWith("ku", StringComparison.OrdinalIgnoreCase)) return "ku";
        if (header.StartsWith("ar", StringComparison.OrdinalIgnoreCase)) return "ar";
        return "en";
    }
}
