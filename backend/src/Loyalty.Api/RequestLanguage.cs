namespace Loyalty.Api;

/// <summary>Resolves the response language from the Accept-Language header (ar → Arabic).</summary>
public static class RequestLanguage
{
    public static bool IsArabic(HttpContext ctx)
    {
        var header = ctx.Request.Headers.AcceptLanguage.ToString();
        return header.TrimStart().StartsWith("ar", StringComparison.OrdinalIgnoreCase);
    }
}
