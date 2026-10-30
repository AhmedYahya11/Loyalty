using System.Text.Json;
using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Accounts.Domain;
using Loyalty.Core.Modules.Applications.Domain;
using Loyalty.Core.Modules.Campaigns.Domain;
using Loyalty.Core.Modules.Integration.Domain;
using Loyalty.Core.Modules.Members.Domain;
using Loyalty.Core.Modules.Settings.Domain;
using Loyalty.Core.Modules.Wallets.Domain;
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
    : DbContext(options)
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
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<PointsLedgerAccount> LedgerAccounts => Set<PointsLedgerAccount>();
    public DbSet<PointsLedgerTransaction> LedgerTransactions => Set<PointsLedgerTransaction>();
    public DbSet<PointsLedgerPosting> LedgerPostings => Set<PointsLedgerPosting>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<IngestionRequest> IngestionRequests => Set<IngestionRequest>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<RewardEffect> RewardEffects => Set<RewardEffect>();

    protected override void OnModelCreating(ModelBuilder b)
    {
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
            e.HasIndex(x => new { x.ApplicationId, x.ExternalCustomerId }).IsUnique();
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
            e.HasIndex(x => new { x.ApplicationId, x.TriggerEventType, x.Status });
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
    }

    /// <summary>Apply the self+descendants global query filter to an IApplicationScoped entity.</summary>
    private void ApplyScope<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> e)
        where T : class, IApplicationScoped
    {
        e.HasQueryFilter(x => ScopeBypassed || ScopeVisibleApplicationIds.Contains(x.ApplicationId));
    }
}
