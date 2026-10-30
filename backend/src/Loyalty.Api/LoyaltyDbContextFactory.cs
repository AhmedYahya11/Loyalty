using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Loyalty.Api;

/// <summary>
/// Design-time factory used by `dotnet ef migrations`. Supplies options + a dummy application
/// context (migrations build the model but never execute scoped queries).
/// </summary>
public sealed class LoyaltyDbContextFactory : IDesignTimeDbContextFactory<LoyaltyDbContext>
{
    public LoyaltyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LoyaltyDbContext>()
            .UseNpgsql("Host=localhost;Database=loyalty_design;Username=loyalty;Password=loyalty",
                npg => npg.MigrationsHistoryTable("__ef_migrations", "public"))
            .Options;
        return new LoyaltyDbContext(options, new ApplicationContext());
    }
}
