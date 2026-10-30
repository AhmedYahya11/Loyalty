using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Accounts.Application;
using Loyalty.Core.Modules.Applications.Application;
using Loyalty.Core.Modules.Campaigns.Application;
using Loyalty.Core.Modules.Integration.Application;
using Loyalty.Core.Modules.Integration.Domain;
using Loyalty.Core.Modules.Members.Application;
using Loyalty.Core.Modules.Rules;
using Loyalty.Core.Modules.Settings.Application;
using Loyalty.Core.Modules.Wallets.Application;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.EntityFrameworkCore;

namespace Loyalty.Core;

public static class DependencyInjection
{
    public static IServiceCollection AddLoyaltyCore(this IServiceCollection services, string connectionString)
    {
        // DbContext with Wolverine transactional-outbox integration
        services.AddDbContextWithWolverineIntegration<LoyaltyDbContext>(o =>
            o.UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable("__ef_migrations", "public")));

        // ambient application scope (same instance behind the interface)
        services.AddScoped<ApplicationContext>();
        services.AddScoped<IApplicationContext>(sp => sp.GetRequiredService<ApplicationContext>());

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<EventRegistry>();
        services.AddSingleton<ILoyaltyRuleEvaluator, OperatorRuleEvaluator>();

        // module services
        services.AddScoped<ApplicationService>();
        services.AddScoped<ApplicationScopeResolver>();
        services.AddScoped<AccountService>();
        services.AddScoped<LoyaltySettingsService>();
        services.AddScoped<LedgerService>();
        services.AddScoped<MemberProvisioningService>();
        services.AddScoped<MemberService>();
        services.AddScoped<CampaignService>();
        services.AddScoped<RewardDecisionService>();
        services.AddScoped<EventIngestionService>();

        return services;
    }
}
