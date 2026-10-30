using System.Security.Cryptography;
using System.Text;

namespace Loyalty.Core.Modules.Notifications.Application;

/// <summary>HMAC-SHA256 signing of webhook bodies. Receivers verify with the shared secret.</summary>
public static class WebhookSigner
{
    public static string Sign(string secret, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        return "sha256=" + Convert.ToHexStringLower(hash);
    }

    public static string NewSecret()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return "whsec_" + Convert.ToHexStringLower(bytes);
    }
}
