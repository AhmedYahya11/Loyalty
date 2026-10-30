using System.Security.Cryptography;
using System.Text;

namespace Loyalty.Core.BuildingBlocks;

public static class HashUtil
{
    public static string Sha256Hex(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
