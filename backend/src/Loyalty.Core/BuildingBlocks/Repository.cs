using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Loyalty.Core.BuildingBlocks;

/// <summary>
/// Generic repository over an entity/aggregate type. Backed by the shared scoped
/// <see cref="LoyaltyDbContext"/>, so every repository participates in the same unit of work and the
/// Wolverine EF-core outbox transaction. <see cref="Query"/> returns a composable IQueryable (the
/// tenant global query filter still applies) for reads/projections/aggregates; writes go through
/// Add/Remove; persistence and explicit transactions are the <see cref="IUnitOfWork"/>'s job.
/// </summary>
public interface IRepository<T> where T : class
{
    /// <summary>Tracking query — compose with Where/Include/Select/ExecuteUpdate, etc.</summary>
    IQueryable<T> Query();
    /// <summary>No-tracking query for pure reads/projections.</summary>
    IQueryable<T> QueryNoTracking();
    ValueTask<T?> FindAsync(CancellationToken ct, params object[] keyValues);
    void Add(T entity);
    void AddRange(IEnumerable<T> entities);
    void AddRange(params T[] entities);
    void Remove(T entity);
    void RemoveRange(IEnumerable<T> entities);
}

/// <summary>EF Core implementation of <see cref="IRepository{T}"/> — the shared base repository.</summary>
public sealed class EfRepository<T>(LoyaltyDbContext db) : IRepository<T> where T : class
{
    public IQueryable<T> Query() => db.Set<T>();
    public IQueryable<T> QueryNoTracking() => db.Set<T>().AsNoTracking();
    public ValueTask<T?> FindAsync(CancellationToken ct, params object[] keyValues) => db.Set<T>().FindAsync(keyValues, ct);
    public void Add(T entity) => db.Set<T>().Add(entity);
    public void AddRange(IEnumerable<T> entities) => db.Set<T>().AddRange(entities);
    public void AddRange(params T[] entities) => db.Set<T>().AddRange(entities);
    public void Remove(T entity) => db.Set<T>().Remove(entity);
    public void RemoveRange(IEnumerable<T> entities) => db.Set<T>().RemoveRange(entities);
}

/// <summary>
/// Unit of work over the shared DbContext: commit the tracked changes and open explicit
/// transactions. Repositories mutate; the unit of work persists — one commit spans every repository
/// resolved in the same scope (they share the DbContext instance).
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default);
}

public sealed class UnitOfWork(LoyaltyDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default) => db.Database.BeginTransactionAsync(ct);
}
