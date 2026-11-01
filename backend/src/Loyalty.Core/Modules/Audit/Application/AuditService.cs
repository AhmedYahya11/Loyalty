using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Audit.Domain;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Audit.Application;

/// <summary>Writes and reads the unified admin audit trail (B5). Application-scoped via the repository's
/// global query filter, so reads only return the caller's tenant (self + descendants).</summary>
public sealed class AuditService(IRepository<AuditEntry> auditRepo, IUnitOfWork uow, IClock clock)
{
    public async Task RecordAsync(
        Guid applicationId, Guid adminUserId, string username, string role,
        string method, string path, string? targetId, int statusCode, CancellationToken ct)
    {
        auditRepo.Add(AuditEntry.Create(applicationId, adminUserId, username, role, method, path, targetId, statusCode, clock.UtcNow));
        await uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<object>> ListAsync(int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 200);
        return await auditRepo.Query().AsNoTracking()
            .OrderByDescending(a => a.OccurredAt)
            .Take(limit)
            .Select(a => (object)new { a.Id, a.Username, a.Role, a.Method, a.Path, targetId = a.TargetId, a.StatusCode, a.OccurredAt })
            .ToListAsync(ct);
    }
}
