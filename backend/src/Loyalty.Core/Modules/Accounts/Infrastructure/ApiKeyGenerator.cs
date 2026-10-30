using System.Security.Cryptography;

namespace Loyalty.Core.Modules.Accounts.Infrastructure;

/// <summary>
/// Generates, hashes, and verifies API keys. At least 32 cryptographically random bytes.
/// The complete key is returned once at creation/rotation; only a searchable prefix and a
/// cryptographic hash are stored. Hash comparison is constant-time (MVP spec §5).
/// </summary>
public static class ApiKeyGenerator
{
    public const string LivePrefix = "lk_live_";
    public const int PrefixLength = 11; // "lk_live_" + 3 searchable chars

    public sealed record GeneratedKey(string RawKey, string KeyPrefix, string KeyHash);

    public static GeneratedKey Generate()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        var secret = Convert.ToHexStringLower(bytes);
        var rawKey = LivePrefix + secret;
        var keyPrefix = rawKey[..PrefixLength];
        var keyHash = Hash(rawKey);
        return new GeneratedKey(rawKey, keyPrefix, keyHash);
    }

    public static string Hash(string rawKey)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexStringLower(hash);
    }

    public static bool Verify(string rawKey, string storedHash)
    {
        var computed = Hash(rawKey);
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(computed),
            System.Text.Encoding.UTF8.GetBytes(storedHash));
    }

    public static string PrefixOf(string rawKey) =>
        rawKey.Length >= PrefixLength ? rawKey[..PrefixLength] : rawKey;
}
