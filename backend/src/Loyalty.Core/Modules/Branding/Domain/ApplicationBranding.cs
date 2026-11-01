using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Branding.Domain;

/// <summary>
/// Per-tenant white-label branding (B6) for the customer-facing card app: display name, logo and theme
/// colors. Application-scoped, one row per application (PK = ApplicationId). Colors are validated hex so
/// they're safe to drop into the client's CSS.
/// </summary>
public sealed class ApplicationBranding : IApplicationScoped
{
    public Guid ApplicationId { get; private set; }
    public string BrandName { get; private set; } = default!;
    public string? LogoUrl { get; private set; }
    public string? PrimaryColor { get; private set; }
    public string? AccentColor { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ApplicationBranding() { }

    public static ApplicationBranding Create(Guid applicationId, string brandName, string? logoUrl, string? primary, string? accent, DateTimeOffset now) => new()
    {
        ApplicationId = applicationId,
        BrandName = brandName,
        LogoUrl = logoUrl,
        PrimaryColor = primary,
        AccentColor = accent,
        UpdatedAt = now,
    };

    public void Update(string brandName, string? logoUrl, string? primary, string? accent, DateTimeOffset now)
    {
        BrandName = brandName;
        LogoUrl = logoUrl;
        PrimaryColor = primary;
        AccentColor = accent;
        UpdatedAt = now;
    }
}
