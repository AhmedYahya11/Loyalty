using System.Text.Json;
using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Accounts.Domain;
using Loyalty.Core.Modules.Applications.Domain;
using Loyalty.Core.Modules.Campaigns.Domain;
using Loyalty.Core.Modules.Cvm.Domain;
using Loyalty.Core.Modules.Gamification.Domain;
using Loyalty.Core.Modules.Integration.Domain;
using Loyalty.Core.Modules.Members.Domain;
using Loyalty.Core.Modules.Notifications.Domain;
using Loyalty.Core.Modules.Rewards.Domain;
using Loyalty.Core.Modules.Settings.Domain;
using Loyalty.Core.Modules.Tiers.Domain;
using Loyalty.Core.Modules.Wallets.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Loyalty.Core.Persistence;

/// <summary>
/// Single database, module schemas (MVP spec §17). A global query filter restricts every
/// IApplicationScoped entity to the authenticated application and its descendants
/// (self + descendants), bypassed only in the narrow system scope (§7).
/// </summary>
public sealed class LoyaltyDbContext(DbContextOptions<LoyaltyDbContext> options, IApplicationContext appContext)
    : IdentityDbContext<Modules.Identity.Domain.ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    private readonly IApplicationContext _appContext = appContext;

    // Scope members referenced by the global query filter (evaluated per query by EF).
    public bool ScopeBypassed => _appContext.IsSystem;
    public Guid[] ScopeVisibleApplicationIds => [.. _appContext.VisibleApplicationIds];

    public DbSet<Application> Applications => Set<Application>();
    public DbSet<ApplicationClosure> ApplicationClosures => Set<ApplicationClosure>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<ApplicationLoyaltySettings> LoyaltySettings => Set<ApplicationLoyaltySettings>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<MemberToken> MemberTokens => Set<MemberToken>();
    // ApplicationUser / roles are exposed via Identity's Users/Roles sets (UserManager/RoleManager)
    public DbSet<Modules.Identity.Domain.AdminSession> AdminSessions => Set<Modules.Identity.Domain.AdminSession>();
    public DbSet<Modules.Audit.Domain.AuditEntry> AuditEntries => Set<Modules.Audit.Domain.AuditEntry>();
    public DbSet<Modules.Connectors.Domain.Connector> Connectors => Set<Modules.Connectors.Domain.Connector>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<PointsLedgerAccount> LedgerAccounts => Set<PointsLedgerAccount>();
    public DbSet<PointsLedgerTransaction> LedgerTransactions => Set<PointsLedgerTransaction>();
    public DbSet<PointsLedgerPosting> LedgerPostings => Set<PointsLedgerPosting>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignUsage> CampaignUsages => Set<CampaignUsage>();
    public DbSet<IngestionRequest> IngestionRequests => Set<IngestionRequest>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<RewardEffect> RewardEffects => Set<RewardEffect>();
    public DbSet<ApplicationCvmSettings> CvmSettings => Set<ApplicationCvmSettings>();
    public DbSet<MemberValueProfile> ValueProfiles => Set<MemberValueProfile>();
    public DbSet<SegmentDefinition> Segments => Set<SegmentDefinition>();
    public DbSet<SegmentVersion> SegmentVersions => Set<SegmentVersion>();
    public DbSet<MemberSegmentMembership> SegmentMemberships => Set<MemberSegmentMembership>();
    public DbSet<Tier> Tiers => Set<Tier>();
    public DbSet<TierHistory> TierHistory => Set<TierHistory>();
    public DbSet<Reward> Rewards => Set<Reward>();
    public DbSet<Redemption> Redemptions => Set<Redemption>();
    public DbSet<Achievement> Achievements => Set<Achievement>();
    public DbSet<MemberAchievement> MemberAchievements => Set<MemberAchievement>();
    public DbSet<Challenge> Challenges => Set<Challenge>();
    public DbSet<MemberChallengeProgress> ChallengeProgress => Set<MemberChallengeProgress>();
    public DbSet<Modules.Engagement.Domain.EngagementSettings> EngagementSettings => Set<Modules.Engagement.Domain.EngagementSettings>();
    public DbSet<Modules.Engagement.Domain.Referral> Referrals => Set<Modules.Engagement.Domain.Referral>();
    public DbSet<Modules.Engagement.Domain.StampCard> StampCards => Set<Modules.Engagement.Domain.StampCard>();
    public DbSet<Modules.Engagement.Domain.MemberStampCard> MemberStampCards => Set<Modules.Engagement.Domain.MemberStampCard>();
    public DbSet<Modules.Raffles.Domain.Raffle> Raffles => Set<Modules.Raffles.Domain.Raffle>();
    public DbSet<Modules.Raffles.Domain.RaffleEntry> RaffleEntries => Set<Modules.Raffles.Domain.RaffleEntry>();
    public DbSet<EventLogEntry> EventLog => Set<EventLogEntry>();
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();
    public DbSet<RewardDecisionRecord> RewardDecisions => Set<RewardDecisionRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b); // ASP.NET Core Identity tables (AspNetUsers/Roles/…)

        // keep Identity tables in a dedicated schema + extra ApplicationUser columns
        b.Entity<Modules.Identity.Domain.ApplicationUser>(e =>
        {
            e.ToTable("users", "identity");
            e.Property(x => x.ApplicationId);
            e.HasIndex(x => x.ApplicationId);
        });
        b.Entity<IdentityRole<Guid>>(e => e.ToTable("roles", "identity"));
        b.Entity<IdentityUserRole<Guid>>(e => e.ToTable("user_roles", "identity"));
        b.Entity<IdentityUserClaim<Guid>>(e => e.ToTable("user_claims", "identity"));
        b.Entity<IdentityUserLogin<Guid>>(e => e.ToTable("user_logins", "identity"));
        b.Entity<IdentityUserToken<Guid>>(e => e.ToTable("user_tokens", "identity"));
        b.Entity<IdentityRoleClaim<Guid>>(e => e.ToTable("role_claims", "identity"));

        // ---------------- applications ----------------
        b.Entity<Application>(e =>
        {
            e.ToTable("applications", "applications");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.NameAr).HasMaxLength(200);
            e.HasIndex(x => new { x.ParentId, x.Name }).IsUnique(); // no duplicate sibling names
        });

        b.Entity<ApplicationClosure>(e =>
        {
            e.ToTable("application_closure", "applications");
            e.HasKey(x => new { x.AncestorApplicationId, x.DescendantApplicationId });
            e.HasIndex(x => x.DescendantApplicationId);
        });

        // ---------------- accounts ----------------
        b.Entity<Account>(e =>
        {
            e.ToTable("accounts", "accounts");
            e.HasKey(x => x.Id);
            e.Property(x => x.AccountName).HasMaxLength(200).IsRequired();
            e.Property(x => x.KeyPrefix).HasMaxLength(32).IsRequired();
            e.Property(x => x.KeyHash).HasMaxLength(128).IsRequired();
            e.HasIndex(x => new { x.ApplicationId, x.AccountName }).IsUnique();
            e.HasIndex(x => x.KeyPrefix).IsUnique();
            ApplyScope(e);
        });

        // ---------------- settings ----------------
        b.Entity<ApplicationLoyaltySettings>(e =>
        {
            e.ToTable("loyalty_settings", "settings");
            e.HasKey(x => x.ApplicationId);
            e.Property(x => x.PointsUnitCode).HasMaxLength(16).IsRequired();
            e.Property(x => x.BaseCurrency).HasMaxLength(8).IsRequired();
            e.Property(x => x.SpendAmountPerPoint).HasPrecision(18, 4);
            e.Property(x => x.RoundingPolicy).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.LoyaltyStatus).HasConversion<string>().HasMaxLength(16);
            ApplyScope(e);
        });

        // ---------------- members ----------------
        b.Entity<Member>(e =>
        {
            e.ToTable("members", "members");
            e.HasKey(x => x.Id);
            e.Property(x => x.ExternalCustomerId).HasMaxLength(200).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.ReferralCode).HasMaxLength(32);
            e.HasIndex(x => new { x.ApplicationId, x.ExternalCustomerId }).IsUnique();
            e.HasIndex(x => new { x.ApplicationId, x.ReferralCode }).IsUnique();
            ApplyScope(e);
        });

        b.Entity<MemberToken>(e =>
        {
            e.ToTable("member_tokens", "members");
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.ApplicationId, x.MemberId });
            ApplyScope(e);
        });

        // ---------------- wallets + ledger ----------------
        b.Entity<Wallet>(e =>
        {
            e.ToTable("wallets", "wallets");
            e.HasKey(x => x.Id);
            e.Property(x => x.PointsUnitCode).HasMaxLength(16).IsRequired();
            e.Property(x => x.WalletType).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.ApplicationId, x.MemberId, x.WalletType }).IsUnique();
            ApplyScope(e);
        });

        b.Entity<PointsLedgerAccount>(e =>
        {
            e.ToTable("points_ledger_accounts", "wallets");
            e.HasKey(x => x.Id);
            e.Property(x => x.AccountCode).HasMaxLength(80).IsRequired();
            e.Property(x => x.AccountType).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.BalanceBucket).HasConversion<string?>().HasMaxLength(16);
            e.Property(x => x.NormalBalance).HasConversion<string>().HasMaxLength(8);
            e.Property(x => x.PointsUnitCode).HasMaxLength(16).IsRequired();
            e.HasIndex(x => new { x.ApplicationId, x.AccountCode }).IsUnique();
            ApplyScope(e);
        });

        b.Entity<PointsLedgerTransaction>(e =>
        {
            e.ToTable("points_ledger_transactions", "wallets");
            e.HasKey(x => x.Id);
            e.Property(x => x.TransactionType).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.PointsUnitCode).HasMaxLength(16).IsRequired();
            e.Property(x => x.SourceSystem).HasMaxLength(200).IsRequired();
            e.Property(x => x.SourceTransactionId).HasMaxLength(200).IsRequired();
            e.Property(x => x.BusinessEventKey).HasMaxLength(400).IsRequired();
            e.Property(x => x.CorrelationId).HasMaxLength(200).IsRequired();
            e.Property(x => x.EffectId).HasMaxLength(200).IsRequired();
            e.HasMany(x => x.Postings).WithOne().HasForeignKey(p => p.LedgerTransactionId);
            // ledger transaction idempotency (§9.4)
            e.HasIndex(x => new { x.ApplicationId, x.WalletId, x.TransactionType, x.SourceSystem, x.BusinessEventKey, x.EffectId })
                .IsUnique();
            ApplyScope(e);
        });

        b.Entity<PointsLedgerPosting>(e =>
        {
            e.ToTable("points_ledger_postings", "wallets", t =>
            {
                t.HasCheckConstraint("ck_posting_nonnegative", "\"DebitAmount\" >= 0 AND \"CreditAmount\" >= 0");
                t.HasCheckConstraint("ck_posting_one_sided",
                    "(\"DebitAmount\" > 0) <> (\"CreditAmount\" > 0)");
            });
            e.HasKey(x => x.Id);
            ApplyScope(e);
        });

        // ---------------- campaigns ----------------
        var condConverter = new ValueConverter<List<CampaignCondition>, string>(
            v => JsonSerializer.Serialize(v, JsonOptionsProvider.Default),
            v => JsonSerializer.Deserialize<List<CampaignCondition>>(v, JsonOptionsProvider.Default) ?? new());
        var condComparer = new ValueComparer<List<CampaignCondition>>(
            (a, c) => JsonSerializer.Serialize(a, JsonOptionsProvider.Default) == JsonSerializer.Serialize(c, JsonOptionsProvider.Default),
            v => JsonSerializer.Serialize(v, JsonOptionsProvider.Default).GetHashCode(),
            v => JsonSerializer.Deserialize<List<CampaignCondition>>(JsonSerializer.Serialize(v, JsonOptionsProvider.Default), JsonOptionsProvider.Default)!);

        var effConverter = new ValueConverter<List<CampaignEffect>, string>(
            v => JsonSerializer.Serialize(v, JsonOptionsProvider.Default),
            v => JsonSerializer.Deserialize<List<CampaignEffect>>(v, JsonOptionsProvider.Default) ?? new());
        var effComparer = new ValueComparer<List<CampaignEffect>>(
            (a, c) => JsonSerializer.Serialize(a, JsonOptionsProvider.Default) == JsonSerializer.Serialize(c, JsonOptionsProvider.Default),
            v => JsonSerializer.Serialize(v, JsonOptionsProvider.Default).GetHashCode(),
            v => JsonSerializer.Deserialize<List<CampaignEffect>>(JsonSerializer.Serialize(v, JsonOptionsProvider.Default), JsonOptionsProvider.Default)!);

        b.Entity<Campaign>(e =>
        {
            e.ToTable("campaigns", "campaigns");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.NameAr).HasMaxLength(200);
            e.Property(x => x.TriggerEventType).HasMaxLength(100).IsRequired();
            e.Property(x => x.StackingPolicy).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Conditions).HasConversion(condConverter, condComparer).HasColumnType("jsonb");
            e.Property(x => x.Effects).HasConversion(effConverter, effComparer).HasColumnType("jsonb");
            e.Property(x => x.RequiredLifecycleStages).HasConversion(JsonListConverter<string>(), JsonListComparer<string>()).HasColumnType("jsonb");
            e.Property(x => x.RequiredSegmentIds).HasConversion(JsonListConverter<Guid>(), JsonListComparer<Guid>()).HasColumnType("jsonb");
            e.Property(x => x.ExcludedSegmentIds).HasConversion(JsonListConverter<Guid>(), JsonListComparer<Guid>()).HasColumnType("jsonb");
            e.Property(x => x.MemberLimits).HasConversion(JsonListConverter<MemberLimit>(), JsonListComparer<MemberLimit>()).HasColumnType("jsonb");
            e.HasIndex(x => new { x.ApplicationId, x.TriggerEventType, x.Status });
            ApplyScope(e);
        });

        b.Entity<CampaignUsage>(e =>
        {
            e.ToTable("campaign_usage", "campaigns");
            e.HasKey(x => x.Id);
            e.Property(x => x.PeriodKey).HasMaxLength(16).IsRequired();
            e.HasIndex(x => new { x.ApplicationId, x.CampaignId, x.MemberId, x.PeriodKey }).IsUnique();
            ApplyScope(e);
        });

        // ---------------- integration ----------------
        b.Entity<IngestionRequest>(e =>
        {
            e.ToTable("ingestion_requests", "integration");
            e.HasKey(x => x.Id);
            e.Property(x => x.SourceSystem).HasMaxLength(200).IsRequired();
            e.Property(x => x.EventType).HasMaxLength(100).IsRequired();
            e.Property(x => x.SourceTransactionId).HasMaxLength(200).IsRequired();
            e.Property(x => x.BusinessEventKey).HasMaxLength(400).IsRequired();
            e.Property(x => x.CustomerId).HasMaxLength(200).IsRequired();
            e.Property(x => x.CorrelationId).HasMaxLength(200).IsRequired();
            e.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.SettingsSnapshot).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.CvmSettingsSnapshot).HasColumnType("jsonb");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.PointsUnitCode).HasMaxLength(16);
            // source-event identity (§9.2) and business-event identity (§9.3)
            e.HasIndex(x => new { x.ApplicationId, x.SourceSystem, x.EventId }).IsUnique();
            e.HasIndex(x => new { x.ApplicationId, x.EventType, x.BusinessEventKey }).IsUnique();
            ApplyScope(e);
        });

        b.Entity<IdempotencyRecord>(e =>
        {
            e.ToTable("idempotency_records", "integration");
            e.HasKey(x => x.Id);
            e.Property(x => x.OperationName).HasMaxLength(100).IsRequired();
            e.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
            e.Property(x => x.RequestPayloadHash).HasMaxLength(128).IsRequired();
            e.Property(x => x.ResponseBody).HasColumnType("jsonb").IsRequired();
            e.HasIndex(x => new { x.ApplicationId, x.AccountId, x.OperationName, x.IdempotencyKey }).IsUnique();
            ApplyScope(e);
        });

        b.Entity<RewardEffect>(e =>
        {
            e.ToTable("reward_effects", "integration");
            e.HasKey(x => x.Id);
            e.Property(x => x.EffectId).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.ApplicationId, x.MemberId, x.SourceEventId, x.CampaignId, x.CampaignVersion, x.EffectId })
                .IsUnique();
            ApplyScope(e);
        });

        // ---------------- cvm ----------------
        b.Entity<ApplicationCvmSettings>(e =>
        {
            e.ToTable("cvm_settings", "cvm");
            e.HasKey(x => x.ApplicationId);
            e.Property(x => x.CvmStatus).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.HighValueNetSpendThreshold).HasPrecision(18, 4);
            ApplyScope(e);
        });

        b.Entity<MemberValueProfile>(e =>
        {
            e.ToTable("member_value_profiles", "cvm");
            e.HasKey(x => new { x.ApplicationId, x.MemberId });
            e.Property(x => x.BaseCurrency).HasMaxLength(8).IsRequired();
            e.Property(x => x.NetSpendAmount).HasPrecision(18, 4);
            e.Property(x => x.LifecycleStage).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Version).IsConcurrencyToken();
            ApplyScope(e);
        });

        var segCondConverter = new ValueConverter<List<CampaignCondition>, string>(
            v => JsonSerializer.Serialize(v, JsonOptionsProvider.Default),
            v => JsonSerializer.Deserialize<List<CampaignCondition>>(v, JsonOptionsProvider.Default) ?? new());
        var segCondComparer = new ValueComparer<List<CampaignCondition>>(
            (a, c) => JsonSerializer.Serialize(a, JsonOptionsProvider.Default) == JsonSerializer.Serialize(c, JsonOptionsProvider.Default),
            v => JsonSerializer.Serialize(v, JsonOptionsProvider.Default).GetHashCode(),
            v => JsonSerializer.Deserialize<List<CampaignCondition>>(JsonSerializer.Serialize(v, JsonOptionsProvider.Default), JsonOptionsProvider.Default)!);

        b.Entity<SegmentDefinition>(e =>
        {
            e.ToTable("segment_definitions", "cvm");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.NameAr).HasMaxLength(200);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(x => new { x.ApplicationId, x.Name }).IsUnique();
            ApplyScope(e);
        });

        b.Entity<SegmentVersion>(e =>
        {
            e.ToTable("segment_versions", "cvm");
            e.HasKey(x => x.Id);
            e.Property(x => x.Conditions).HasConversion(segCondConverter, segCondComparer).HasColumnType("jsonb");
            e.HasIndex(x => new { x.ApplicationId, x.SegmentId, x.Version }).IsUnique();
            ApplyScope(e);
        });

        b.Entity<MemberSegmentMembership>(e =>
        {
            e.ToTable("member_segment_memberships", "cvm");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ApplicationId, x.MemberId, x.SegmentId, x.IsActive });
            ApplyScope(e);
        });

        // ---------------- tiers ----------------
        b.Entity<Tier>(e =>
        {
            e.ToTable("tiers", "tiers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.NameAr).HasMaxLength(100);
            e.Property(x => x.Color).HasMaxLength(32);
            e.Property(x => x.ThresholdMetric).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.ThresholdValue).HasPrecision(18, 4);
            e.Property(x => x.EarnMultiplier).HasPrecision(9, 4);
            e.HasIndex(x => new { x.ApplicationId, x.Rank }).IsUnique();
            e.HasIndex(x => new { x.ApplicationId, x.Name }).IsUnique();
            ApplyScope(e);
        });

        b.Entity<TierHistory>(e =>
        {
            e.ToTable("tier_history", "tiers");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ApplicationId, x.MemberId, x.EvaluatedAt });
            ApplyScope(e);
        });

        // ---------------- rewards + redemptions ----------------
        b.Entity<Reward>(e =>
        {
            e.ToTable("rewards", "rewards");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.NameAr).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.RewardType).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Value).HasPrecision(18, 4);
            e.HasIndex(x => new { x.ApplicationId, x.IsActive });
            ApplyScope(e);
        });

        b.Entity<Redemption>(e =>
        {
            e.ToTable("redemptions", "rewards");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.CouponCode).HasMaxLength(32).IsRequired();
            e.HasIndex(x => new { x.ApplicationId, x.CouponCode }).IsUnique();
            e.HasIndex(x => new { x.ApplicationId, x.MemberId, x.Status });
            ApplyScope(e);
        });

        // ---------------- gamification ----------------
        b.Entity<Achievement>(e =>
        {
            e.ToTable("achievements", "gamification");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.NameAr).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.Icon).HasMaxLength(64);
            e.Property(x => x.Metric).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Threshold).HasPrecision(18, 4);
            e.HasIndex(x => new { x.ApplicationId, x.IsActive });
            ApplyScope(e);
        });
        b.Entity<MemberAchievement>(e =>
        {
            e.ToTable("member_achievements", "gamification");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ApplicationId, x.MemberId, x.AchievementId }).IsUnique();
            ApplyScope(e);
        });
        b.Entity<Challenge>(e =>
        {
            e.ToTable("challenges", "gamification");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.NameAr).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.Metric).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.TargetValue).HasPrecision(18, 4);
            e.HasIndex(x => new { x.ApplicationId, x.IsActive });
            ApplyScope(e);
        });
        b.Entity<MemberChallengeProgress>(e =>
        {
            e.ToTable("member_challenge_progress", "gamification");
            e.HasKey(x => x.Id);
            e.Property(x => x.Progress).HasPrecision(18, 4);
            e.HasIndex(x => new { x.ApplicationId, x.MemberId, x.ChallengeId }).IsUnique();
            ApplyScope(e);
        });

        // ---------------- engagement (referrals, stamp cards, birthday) ----------------
        b.Entity<Modules.Engagement.Domain.EngagementSettings>(e =>
        {
            e.ToTable("engagement_settings", "engagement");
            e.HasKey(x => x.ApplicationId);
            ApplyScope(e);
        });
        b.Entity<Modules.Engagement.Domain.Referral>(e =>
        {
            e.ToTable("referrals", "engagement");
            e.HasKey(x => x.Id);
            e.Property(x => x.ReferralKey).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.ApplicationId, x.ReferralKey }).IsUnique();
            e.HasIndex(x => new { x.ApplicationId, x.ReferrerMemberId });
            ApplyScope(e);
        });
        b.Entity<Modules.Engagement.Domain.StampCard>(e =>
        {
            e.ToTable("stamp_cards", "engagement");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.NameAr).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.TriggerEventType).HasMaxLength(64).IsRequired();
            e.Property(x => x.Conditions)
                .HasConversion(JsonListConverter<Modules.Campaigns.Domain.CampaignCondition>(), JsonListComparer<Modules.Campaigns.Domain.CampaignCondition>())
                .HasColumnType("jsonb");
            e.HasIndex(x => new { x.ApplicationId, x.IsActive });
            ApplyScope(e);
        });
        b.Entity<Modules.Engagement.Domain.MemberStampCard>(e =>
        {
            e.ToTable("member_stamp_cards", "engagement");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ApplicationId, x.MemberId, x.StampCardId }).IsUnique();
            ApplyScope(e);
        });

        // ---------------- audit trail (B5) — application-scoped, append-only ----------------
        b.Entity<Modules.Audit.Domain.AuditEntry>(e =>
        {
            e.ToTable("entries", "audit");
            e.HasKey(x => x.Id);
            e.Property(x => x.Username).HasMaxLength(100).IsRequired();
            e.Property(x => x.Role).HasMaxLength(16).IsRequired();
            e.Property(x => x.Method).HasMaxLength(10).IsRequired();
            e.Property(x => x.Path).HasMaxLength(256).IsRequired();
            e.Property(x => x.TargetId).HasMaxLength(64);
            e.HasIndex(x => new { x.ApplicationId, x.OccurredAt });
            ApplyScope(e);
        });

        // ---------------- connectors (inbound e-commerce/POS webhooks, B2) — application-scoped ----------------
        b.Entity<Modules.Connectors.Domain.Connector>(e =>
        {
            e.ToTable("connectors", "connectors");
            e.HasKey(x => x.Id);
            e.Property(x => x.Platform).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Secret).HasMaxLength(128).IsRequired();
            e.HasIndex(x => x.ApplicationId);
            ApplyScope(e);
        });

        // ---------------- identity (dashboard admin sessions) — NOT application-scoped ----------------
        b.Entity<Modules.Identity.Domain.AdminSession>(e =>
        {
            e.ToTable("admin_sessions", "identity");
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.AdminUserId);
        });

        // ---------------- raffles (prize draws) ----------------
        b.Entity<Modules.Raffles.Domain.Raffle>(e =>
        {
            e.ToTable("raffles", "raffles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.NameAr).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.TriggerEventType).HasMaxLength(64).IsRequired();
            e.Property(x => x.PrizeDescription).HasMaxLength(500);
            e.Property(x => x.MinAmount).HasPrecision(18, 4);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Conditions)
                .HasConversion(JsonListConverter<Modules.Campaigns.Domain.CampaignCondition>(), JsonListComparer<Modules.Campaigns.Domain.CampaignCondition>())
                .HasColumnType("jsonb");
            e.HasIndex(x => new { x.ApplicationId, x.Status });
            ApplyScope(e);
        });
        b.Entity<Modules.Raffles.Domain.RaffleEntry>(e =>
        {
            e.ToTable("raffle_entries", "raffles");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ApplicationId, x.RaffleId, x.SourceEventId }).IsUnique();
            e.HasIndex(x => new { x.RaffleId, x.MemberId });
            ApplyScope(e);
        });

        // ---------------- notifications ----------------
        b.Entity<EventLogEntry>(e =>
        {
            e.ToTable("event_log", "notifications");
            e.HasKey(x => x.Id);
            e.Property(x => x.EventType).HasMaxLength(64).IsRequired();
            e.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
            e.HasIndex(x => new { x.ApplicationId, x.DeliveredAt });
            e.HasIndex(x => new { x.ApplicationId, x.CreatedAt });
            ApplyScope(e);
        });

        b.Entity<WebhookSubscription>(e =>
        {
            e.ToTable("webhook_subscriptions", "notifications");
            e.HasKey(x => x.Id);
            e.Property(x => x.Url).HasMaxLength(500).IsRequired();
            e.Property(x => x.Secret).HasMaxLength(200).IsRequired();
            e.Property(x => x.EventTypes).HasConversion(JsonListConverter<string>(), JsonListComparer<string>()).HasColumnType("jsonb");
            ApplyScope(e);
        });

        b.Entity<RewardDecisionRecord>(e =>
        {
            e.ToTable("reward_decisions", "campaigns");
            e.HasKey(x => x.Id);
            e.Property(x => x.LifecycleStage).HasMaxLength(16).IsRequired();
            e.Property(x => x.Outcome).HasMaxLength(32).IsRequired();
            e.Property(x => x.SegmentVersions).HasConversion(JsonListConverter<SegmentVersionRef>(), JsonListComparer<SegmentVersionRef>()).HasColumnType("jsonb");
            e.HasIndex(x => new { x.ApplicationId, x.MemberId, x.CreatedAt });
            ApplyScope(e);
        });
    }

    /// <summary>Apply the self+descendants global query filter to an IApplicationScoped entity.</summary>
    private void ApplyScope<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> e)
        where T : class, IApplicationScoped
    {
        e.HasQueryFilter(x => ScopeBypassed || ScopeVisibleApplicationIds.Contains(x.ApplicationId));
    }

    private static ValueConverter<List<T>, string> JsonListConverter<T>() => new(
        v => JsonSerializer.Serialize(v, JsonOptionsProvider.Default),
        v => ParseJsonList<T>(v));

    /// <summary>Deserialize a jsonb list column, tolerating a non-array value (e.g. an empty "{}"
    /// default) by returning an empty list instead of throwing.</summary>
    private static List<T> ParseJsonList<T>(string v)
    {
        if (string.IsNullOrWhiteSpace(v) || v.TrimStart().StartsWith('{')) return new();
        try { return JsonSerializer.Deserialize<List<T>>(v, JsonOptionsProvider.Default) ?? new(); }
        catch (JsonException) { return new(); }
    }

    private static ValueComparer<List<T>> JsonListComparer<T>() => new(
        (a, c) => JsonSerializer.Serialize(a, JsonOptionsProvider.Default) == JsonSerializer.Serialize(c, JsonOptionsProvider.Default),
        v => JsonSerializer.Serialize(v, JsonOptionsProvider.Default).GetHashCode(),
        v => JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(v, JsonOptionsProvider.Default), JsonOptionsProvider.Default)!);
}
