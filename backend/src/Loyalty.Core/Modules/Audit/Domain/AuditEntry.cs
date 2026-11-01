using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Audit.Domain;

/// <summary>
/// Append-only record of a mutating admin/operator action (B5 — unified audit trail): who, what, when.
/// Application-scoped (a tenant admin sees their own trail; a SuperAdmin on the root sees the subtree).
/// Stores method + path + target id + outcome — never request bodies, to avoid capturing secrets.
/// </summary>
public sealed class AuditEntry : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid AdminUserId { get; private set; }
    public string Username { get; private set; } = default!;
    public string Role { get; private set; } = default!;
    public string Method { get; private set; } = default!;
    public string Path { get; private set; } = default!;
    public string? TargetId { get; private set; }
    public int StatusCode { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    private AuditEntry() { }

    public static AuditEntry Create(
        Guid applicationId, Guid adminUserId, string username, string role,
        string method, string path, string? targetId, int statusCode, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ApplicationId = applicationId,
        AdminUserId = adminUserId,
        Username = username,
        Role = role,
        Method = method,
        Path = path,
        TargetId = targetId,
        StatusCode = statusCode,
        OccurredAt = now,
    };
}
