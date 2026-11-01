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
using Loyalty.Core.Modules.Identity.Domain;
using Loyalty.Core.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine.EntityFrameworkCore;

namespace Loyalty.Core;

public static class DependencyInjection
{
    public static IServiceCollection AddLoyaltyCore(this IServiceCollection services, string connectionString)
    {
        // DbContext with Wolverine transactional-outbox integration
        services.AddDbContextWithWolverineIntegration<LoyaltyDbContext>(o =>
            o.UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable("__ef_migrations", "public")));

        // data access: generic base repository per entity + a unit of work, all over the shared
        // scoped DbContext (so they join the same transaction / Wolverine EF outbox).
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // ASP.NET Core Identity for dashboard users + roles (password hashing, stores, role management).
        // AddIdentityCore (not AddIdentity) — no cookie/sign-in middleware; we keep our own bearer-token
        // auth middleware. Password policy relaxed to length-only to match existing admin credentials.
        services.AddIdentityCore<ApplicationUser>(o =>
        {
            o.Password.RequiredLength = 8;
            o.Password.RequireDigit = false;
            o.Password.RequireLowercase = false;
            o.Password.RequireUppercase = false;
            o.Password.RequireNonAlphanumeric = false;
            o.Password.RequiredUniqueChars = 1;
            o.User.RequireUniqueEmail = false;
            o.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";
        })
        .AddRoles<IdentityRole<Guid>>()
        .AddEntityFrameworkStores<LoyaltyDbContext>();

        // ambient application scope (same instance behind the interface)
        services.AddScoped<ApplicationContext>();
        services.AddScoped<IApplicationContext>(sp => sp.GetRequiredService<ApplicationContext>());
        services.AddScoped<CustomerContext>();
        services.AddScoped<AdminContext>();
        services.AddScoped<Modules.Identity.Application.AdminIdentityService>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<EventRegistry>();
        // Default: deterministic operator evaluator. Set LOYALTY_RULES_ENGINE=rulesengine to use
        // the Microsoft RulesEngine adapter (same ILoyaltyRuleEvaluator port).
        if (string.Equals(Environment.GetEnvironmentVariable("LOYALTY_RULES_ENGINE"), "rulesengine", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<ILoyaltyRuleEvaluator, RulesEngineRuleEvaluator>();
        else
            services.AddSingleton<ILoyaltyRuleEvaluator, OperatorRuleEvaluator>();

        // module services
        services.AddScoped<ApplicationService>();
        services.AddScoped<ApplicationScopeResolver>();
        services.AddScoped<AccountService>();
        services.AddScoped<LoyaltySettingsService>();
        services.AddScoped<Modules.Cvm.Application.CvmSettingsService>();
        services.AddScoped<Modules.Cvm.Application.CvmService>();
        services.AddScoped<Modules.Cvm.Application.SegmentService>();
        services.AddScoped<Modules.Cvm.Application.LifecycleSweepService>();
        services.AddHostedService<Modules.Cvm.Application.LifecycleSweepJob>();
        services.AddScoped<Modules.Cvm.Application.RfmService>();
        services.AddHostedService<Modules.Cvm.Application.RfmJob>();
        services.AddScoped<LedgerService>();
        services.AddScoped<PointsExpiryService>();
        services.AddHostedService<PointsExpiryJob>();
        services.AddScoped<ReconciliationService>();
        services.AddScoped<MemberProvisioningService>();
        services.AddScoped<MemberService>();
        services.AddScoped<MemberTokenService>();
        services.AddScoped<CampaignService>();
        services.AddScoped<CampaignEngine>();
        services.AddScoped<RewardDecisionService>();
        services.AddScoped<EventIngestionService>();

        // notifications + webhooks + analytics (P4)
        services.AddHttpClient();
        services.AddScoped<Modules.Notifications.Application.NotificationService>();
        services.AddScoped<Modules.Notifications.Application.WebhookDispatcher>();
        services.AddHostedService<Modules.Notifications.Application.WebhookDispatcherJob>();
        services.AddScoped<Modules.Analytics.AnalyticsService>();
        services.AddScoped<Modules.Analytics.PromotionAnalyticsService>();
        services.AddScoped<Modules.Tiers.Application.TierService>();
        services.AddScoped<Modules.Rewards.Application.RewardService>();
        services.AddScoped<Modules.Rewards.Application.RedemptionService>();
        services.AddScoped<Modules.Gamification.Application.GamificationService>();
        services.AddScoped<Modules.Gamification.Application.AchievementService>();
        services.AddScoped<Modules.Gamification.Application.ChallengeService>();
        services.AddScoped<Modules.Gamification.Application.LeaderboardService>();

        // engagement: referrals, stamp cards, birthday/scheduled rewards (P10)
        services.AddScoped<Modules.Engagement.Application.EngagementSettingsService>();
        services.AddScoped<Modules.Engagement.Application.ReferralService>();
        services.AddScoped<Modules.Engagement.Application.StampCardService>();
        services.AddScoped<Modules.Engagement.Application.BirthdaySweepService>();
        services.AddHostedService<Modules.Engagement.Application.BirthdaySweepJob>();

        // customer-facing app (P11)
        services.AddScoped<Modules.Customers.Application.CustomerService>();

        // raffles / prize draws (P15)
        services.AddScoped<Modules.Raffles.Application.RaffleService>();

        return services;
    }
}
