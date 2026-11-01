using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Loyalty.Core.BuildingBlocks;

/// <summary>
/// Dashboard admin JWTs (HS256). Tokens are Identity-issued at login and carry the user id, username,
/// role and tenant (<c>app_id</c>) plus a <c>jti</c> that maps to an <see cref="Loyalty.Core.Modules.Identity.Domain.AdminSession"/>
/// so they remain revocable (logout / password change). The same signing key + validation parameters
/// are shared by the issuer (Core) and the JwtBearer validator (API). Key comes from JWT_SIGNING_KEY
/// (or Jwt:Key); a dev fallback is used locally.
/// </summary>
public static class JwtAuth
{
    public const string Issuer = "kinutu";
    public const string Audience = "kinutu";
    public const string RoleClaim = "role";
    public const string NameClaim = "name";
    public const string AppIdClaim = "app_id";

    private static string KeyMaterial =>
        Environment.GetEnvironmentVariable("JWT_SIGNING_KEY") is { Length: >= 32 } k
            ? k
            : "dev-insecure-jwt-signing-key-change-me-0123456789";

    public static SymmetricSecurityKey SigningKey => new(Encoding.UTF8.GetBytes(KeyMaterial));

    public static string CreateToken(Guid userId, string username, string role, Guid applicationId, Guid sessionId, DateTimeOffset expiresAt)
    {
        var creds = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(NameClaim, username),
            new Claim(RoleClaim, role),
            new Claim(AppIdClaim, applicationId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, sessionId.ToString()),
        };
        var token = new JwtSecurityToken(Issuer, Audience, claims, expires: expiresAt.UtcDateTime, signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = SigningKey,
        NameClaimType = NameClaim,
        RoleClaimType = RoleClaim,
        ClockSkew = TimeSpan.FromSeconds(30),
    };
}
