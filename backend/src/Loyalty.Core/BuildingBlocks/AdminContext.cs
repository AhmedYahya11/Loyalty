using Loyalty.Core.Modules.Identity.Domain;

namespace Loyalty.Core.BuildingBlocks;

/// <summary>
/// Request-scoped identity of the dashboard operator for an admin-session (Bearer) request (P17).
/// Established by the auth middleware alongside the <see cref="ApplicationContext"/>. Unset for
/// plain API-key requests. Endpoints read <see cref="Role"/> for RBAC.
/// </summary>
public sealed class AdminContext
{
    public Guid UserId { get; private set; }
    public string Username { get; private set; } = string.Empty;
    public Guid ApplicationId { get; private set; }
    public AdminRole Role { get; private set; }
    public Guid SessionId { get; private set; } // JWT jti → AdminSession, for logout/revocation
    public bool IsEstablished { get; private set; }

    public void Establish(Guid userId, string username, Guid applicationId, AdminRole role, Guid sessionId = default)
    {
        UserId = userId;
        Username = username;
        ApplicationId = applicationId;
        Role = role;
        SessionId = sessionId;
        IsEstablished = true;
    }

    public bool CanManageUsers => IsEstablished && Role >= AdminRole.Admin;
    public bool IsSuperAdmin => IsEstablished && Role == AdminRole.SuperAdmin;
}
