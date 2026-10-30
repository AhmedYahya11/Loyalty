namespace Loyalty.Core.Modules.Applications.Domain;

/// <summary>
/// The tenant / brand / service node and the security boundary. Every business entity
/// belongs to exactly one application. The first application is the root (no parent);
/// every other application has exactly one existing parent (MVP spec §4).
/// Application itself is technical metadata and is NOT application-scoped.
/// </summary>
public sealed class Application
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = default!;
    /// <summary>Optional Arabic display name (bilingual support). الاسم بالعربية.</summary>
    public string? NameAr { get; private set; }
    public Guid? ParentId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Application() { }

    public static Application Create(Guid id, string name, string? nameAr, Guid? parentId, DateTimeOffset now) => new()
    {
        Id = id,
        Name = name.Trim(),
        NameAr = string.IsNullOrWhiteSpace(nameAr) ? null : nameAr.Trim(),
        ParentId = parentId,
        IsActive = true,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void SetActive(bool active, DateTimeOffset now)
    {
        IsActive = active;
        UpdatedAt = now;
    }
}
