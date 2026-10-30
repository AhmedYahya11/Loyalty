using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Applications.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Applications.Application;

public sealed record ApplicationView(Guid Id, string Name, string? NameAr, Guid? ParentId, bool IsActive);

/// <summary>
/// Manages the application hierarchy and its closure table. Creation writes the application
/// row plus all closure rows (self depth 0 + ancestor rows) in one transaction, with
/// guards against cycles, self-parenting, invalid parents, and duplicate sibling names
/// (MVP spec §4). Hierarchy writes run in system scope.
/// </summary>
public sealed class ApplicationService(LoyaltyDbContext db, ApplicationContext scope, IClock clock)
{
    public async Task<ApplicationView> CreateAsync(string name, string? nameAr, Guid? parentId, CancellationToken ct)
    {
        scope.EnterSystemScope();
        try
        {
            name = name.Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new LoyaltyException(LoyaltyError.ValidationFailed("name is required."));

            if (parentId is { } pid)
            {
                var parent = await db.Applications.FirstOrDefaultAsync(a => a.Id == pid, ct)
                    ?? throw new LoyaltyException(LoyaltyError.ValidationFailed("parent application does not exist."));
                var dupe = await db.Applications.AnyAsync(a => a.ParentId == pid && a.Name == name, ct);
                if (dupe) throw new LoyaltyException(LoyaltyError.Conflict("duplicate_sibling_name", "A sibling with this name already exists."));
            }
            else
            {
                var rootExists = await db.Applications.AnyAsync(a => a.ParentId == null, ct);
                if (rootExists)
                    throw new LoyaltyException(LoyaltyError.ValidationFailed("a root application already exists; specify a parentId."));
            }

            var now = clock.UtcNow;
            var id = Guid.NewGuid();
            var app = Domain.Application.Create(id, name, nameAr, parentId, now);
            db.Applications.Add(app);

            // self-row at depth 0
            db.ApplicationClosures.Add(new ApplicationClosure(id, id, 0));
            // inherit ancestor rows from the parent's closure, depth + 1
            if (parentId is { } p)
            {
                var parentAncestors = await db.ApplicationClosures
                    .Where(c => c.DescendantApplicationId == p)
                    .ToListAsync(ct);
                foreach (var a in parentAncestors)
                    db.ApplicationClosures.Add(new ApplicationClosure(a.AncestorApplicationId, id, a.Depth + 1));
            }

            await db.SaveChangesAsync(ct);
            return new ApplicationView(app.Id, app.Name, app.NameAr, app.ParentId, app.IsActive);
        }
        finally { scope.LeaveSystemScope(); }
    }

    public async Task<ApplicationView?> GetAsync(Guid id, CancellationToken ct)
    {
        var a = await db.Applications.FirstOrDefaultAsync(x => x.Id == id, ct);
        return a is null ? null : new ApplicationView(a.Id, a.Name, a.NameAr, a.ParentId, a.IsActive);
    }

    public async Task<IReadOnlyList<ApplicationView>> ListVisibleAsync(CancellationToken ct)
    {
        // visible applications = those in the authenticated subtree
        var visible = scope.VisibleApplicationIds.ToArray();
        return await db.Applications
            .Where(a => visible.Contains(a.Id))
            .OrderBy(a => a.Name)
            .Select(a => new ApplicationView(a.Id, a.Name, a.NameAr, a.ParentId, a.IsActive))
            .ToListAsync(ct);
    }

    public async Task SetStatusAsync(Guid id, bool active, CancellationToken ct)
    {
        scope.EnterSystemScope();
        try
        {
            var a = await db.Applications.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new LoyaltyException(LoyaltyError.NotFound("Application"));
            a.SetActive(active, clock.UtcNow);
            await db.SaveChangesAsync(ct);
        }
        finally { scope.LeaveSystemScope(); }
    }

    /// <summary>One-time bootstrap: create the root application if none exists.</summary>
    public async Task<(ApplicationView App, bool Created)> BootstrapRootAsync(string name, string? nameAr, CancellationToken ct)
    {
        scope.EnterSystemScope();
        try
        {
            var existing = await db.Applications.FirstOrDefaultAsync(a => a.ParentId == null, ct);
            if (existing is not null)
                return (new ApplicationView(existing.Id, existing.Name, existing.NameAr, existing.ParentId, existing.IsActive), false);
        }
        finally { scope.LeaveSystemScope(); }

        var created = await CreateAsync(name, nameAr, null, ct);
        return (created, true);
    }
}
