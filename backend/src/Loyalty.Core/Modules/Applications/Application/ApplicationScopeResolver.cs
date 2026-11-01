using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Applications.Application;

/// <summary>
/// Resolves the visible-application set (self + descendants) and validates the active chain
/// (the target application and every ancestor must be active). Reads unscoped closure and
/// application tables; intended to run inside the narrow system scope (MVP spec §4, §7).
/// </summary>
public sealed class ApplicationScopeResolver(IRepository<Loyalty.Core.Modules.Applications.Domain.Application> applicationsRepo, IRepository<Loyalty.Core.Modules.Applications.Domain.ApplicationClosure> applicationClosuresRepo)
{
    public async Task<Guid[]> ResolveVisibleAsync(Guid applicationId, CancellationToken ct)
    {
        // descendants = closure rows where this application is the ancestor
        return await applicationClosuresRepo.Query()
            .Where(c => c.AncestorApplicationId == applicationId)
            .Select(c => c.DescendantApplicationId)
            .ToArrayAsync(ct);
    }

    /// <summary>True when the application and all of its ancestors are active.</summary>
    public async Task<bool> IsActiveChainAsync(Guid applicationId, CancellationToken ct)
    {
        var ancestorIds = await applicationClosuresRepo.Query()
            .Where(c => c.DescendantApplicationId == applicationId)
            .Select(c => c.AncestorApplicationId)
            .ToArrayAsync(ct);

        if (ancestorIds.Length == 0) return false; // unknown application

        var activeCount = await applicationsRepo.Query()
            .Where(a => ancestorIds.Contains(a.Id) && a.IsActive)
            .CountAsync(ct);

        return activeCount == ancestorIds.Length;
    }

    public Task<bool> IsVisibleAsync(Guid parentApplicationId, Guid targetApplicationId, CancellationToken ct) =>
        applicationClosuresRepo.Query().AnyAsync(
            c => c.AncestorApplicationId == parentApplicationId && c.DescendantApplicationId == targetApplicationId, ct);
}
