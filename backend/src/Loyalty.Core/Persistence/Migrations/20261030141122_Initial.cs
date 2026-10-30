using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Loyalty.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "accounts");

            migrationBuilder.EnsureSchema(
                name: "applications");

            migrationBuilder.EnsureSchema(
                name: "campaigns");

            migrationBuilder.EnsureSchema(
                name: "cvm");

            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.EnsureSchema(
                name: "integration");

            migrationBuilder.EnsureSchema(
                name: "settings");

            migrationBuilder.EnsureSchema(
                name: "members");

            migrationBuilder.EnsureSchema(
                name: "wallets");

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    KeyPrefix = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    KeyHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastAuthenticatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "application_closure",
                schema: "applications",
                columns: table => new
                {
                    AncestorApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DescendantApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Depth = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_closure", x => new { x.AncestorApplicationId, x.DescendantApplicationId });
                });

            migrationBuilder.CreateTable(
                name: "applications",
                schema: "applications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_applications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "campaign_usage",
                schema: "campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodKey = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExecutionCount = table.Column<int>(type: "integer", nullable: false),
                    IssuedPoints = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_campaign_usage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "campaigns",
                schema: "campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TriggerEventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MinimumEventVersion = table.Column<int>(type: "integer", nullable: false),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    StackingPolicy = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Conditions = table.Column<string>(type: "jsonb", nullable: false),
                    Effects = table.Column<string>(type: "jsonb", nullable: false),
                    RequiredLifecycleStages = table.Column<string>(type: "jsonb", nullable: false),
                    RequiredSegmentIds = table.Column<string>(type: "jsonb", nullable: false),
                    ExcludedSegmentIds = table.Column<string>(type: "jsonb", nullable: false),
                    MemberLimits = table.Column<string>(type: "jsonb", nullable: false),
                    GlobalBudgetInitial = table.Column<long>(type: "bigint", nullable: true),
                    GlobalBudgetRemaining = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_campaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cvm_settings",
                schema: "cvm",
                columns: table => new
                {
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CvmStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    NewPeriodDays = table.Column<int>(type: "integer", nullable: false),
                    AtRiskAfterDays = table.Column<int>(type: "integer", nullable: false),
                    DormantAfterDays = table.Column<int>(type: "integer", nullable: false),
                    ChurnedAfterDays = table.Column<int>(type: "integer", nullable: false),
                    HighValueNetSpendThreshold = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    FrequentPurchaseThreshold = table.Column<long>(type: "bigint", nullable: false),
                    EvaluationWindowDays = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cvm_settings", x => x.ApplicationId);
                });

            migrationBuilder.CreateTable(
                name: "event_log",
                schema: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveryAttempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_log", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                schema: "integration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestPayloadHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponseStatusCode = table.Column<int>(type: "integer", nullable: false),
                    ResponseBody = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ingestion_requests",
                schema: "integration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystem = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EventVersion = table.Column<int>(type: "integer", nullable: false),
                    SourceTransactionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BusinessEventKey = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    CustomerId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StatusReason = table.Column<string>(type: "text", nullable: true),
                    ResolvedSettingsApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SettingsVersion = table.Column<int>(type: "integer", nullable: false),
                    SettingsSnapshot = table.Column<string>(type: "jsonb", nullable: false),
                    ResolvedCvmSettingsApplicationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CvmSettingsVersion = table.Column<int>(type: "integer", nullable: true),
                    CvmSettingsSnapshot = table.Column<string>(type: "jsonb", nullable: true),
                    AwardedPoints = table.Column<long>(type: "bigint", nullable: true),
                    PointsUnitCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ingestion_requests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "loyalty_settings",
                schema: "settings",
                columns: table => new
                {
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    PointsUnitCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    BaseCurrency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    SpendAmountPerPoint = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    RoundingPolicy = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LoyaltyStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LoyaltyStartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LoyaltyEndsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loyalty_settings", x => x.ApplicationId);
                });

            migrationBuilder.CreateTable(
                name: "member_segment_memberships",
                schema: "cvm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    SegmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SegmentVersion = table.Column<int>(type: "integer", nullable: false),
                    EnteredProfileVersion = table.Column<int>(type: "integer", nullable: false),
                    LastEvaluatedProfileVersion = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    EnteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExitedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EvaluatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_member_segment_memberships", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "member_value_profiles",
                schema: "cvm",
                columns: table => new
                {
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstActivityAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastPurchaseAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PurchaseCount = table.Column<long>(type: "bigint", nullable: false),
                    NetSpendAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    BaseCurrency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    LoginCount = table.Column<long>(type: "bigint", nullable: false),
                    ReferralCount = table.Column<long>(type: "bigint", nullable: false),
                    PointsEarned = table.Column<long>(type: "bigint", nullable: false),
                    PointsRedeemed = table.Column<long>(type: "bigint", nullable: false),
                    PointsExpired = table.Column<long>(type: "bigint", nullable: false),
                    LifecycleStage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LastLifecycleEvaluationAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_member_value_profiles", x => new { x.ApplicationId, x.MemberId });
                });

            migrationBuilder.CreateTable(
                name: "members",
                schema: "members",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalCustomerId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DefaultWalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_members", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "points_ledger_accounts",
                schema: "wallets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    AccountType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    WalletId = table.Column<Guid>(type: "uuid", nullable: true),
                    BalanceBucket = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    PointsUnitCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    NormalBalance = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_points_ledger_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "points_ledger_transactions",
                schema: "wallets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PointsUnitCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TotalDebit = table.Column<long>(type: "bigint", nullable: false),
                    TotalCredit = table.Column<long>(type: "bigint", nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystem = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceTransactionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BusinessEventKey = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: true),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    EffectId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReversalOfTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_points_ledger_transactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "reward_decisions",
                schema: "campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileVersion = table.Column<int>(type: "integer", nullable: false),
                    LifecycleStage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SegmentVersions = table.Column<string>(type: "jsonb", nullable: false),
                    AwardedPoints = table.Column<long>(type: "bigint", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reward_decisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "reward_effects",
                schema: "integration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignVersion = table.Column<int>(type: "integer", nullable: false),
                    EffectId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Points = table.Column<long>(type: "bigint", nullable: false),
                    LedgerTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reward_effects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "segment_definitions",
                schema: "cvm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CurrentVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_segment_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "segment_versions",
                schema: "cvm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SegmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Conditions = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByAccountId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_segment_versions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "wallets",
                schema: "wallets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PointsUnitCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ActiveBalance = table.Column<long>(type: "bigint", nullable: false),
                    PendingBalance = table.Column<long>(type: "bigint", nullable: false),
                    ReservedBalance = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wallets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_subscriptions",
                schema: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Secret = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EventTypes = table.Column<string>(type: "jsonb", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_subscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "points_ledger_postings",
                schema: "wallets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    LedgerTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LedgerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    DebitAmount = table.Column<long>(type: "bigint", nullable: false),
                    CreditAmount = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_points_ledger_postings", x => x.Id);
                    table.CheckConstraint("ck_posting_nonnegative", "\"DebitAmount\" >= 0 AND \"CreditAmount\" >= 0");
                    table.CheckConstraint("ck_posting_one_sided", "(\"DebitAmount\" > 0) <> (\"CreditAmount\" > 0)");
                    table.ForeignKey(
                        name: "FK_points_ledger_postings_points_ledger_transactions_LedgerTra~",
                        column: x => x.LedgerTransactionId,
                        principalSchema: "wallets",
                        principalTable: "points_ledger_transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounts_ApplicationId_AccountName",
                schema: "accounts",
                table: "accounts",
                columns: new[] { "ApplicationId", "AccountName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounts_KeyPrefix",
                schema: "accounts",
                table: "accounts",
                column: "KeyPrefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_application_closure_DescendantApplicationId",
                schema: "applications",
                table: "application_closure",
                column: "DescendantApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_applications_ParentId_Name",
                schema: "applications",
                table: "applications",
                columns: new[] { "ParentId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_campaign_usage_ApplicationId_CampaignId_MemberId_PeriodKey",
                schema: "campaigns",
                table: "campaign_usage",
                columns: new[] { "ApplicationId", "CampaignId", "MemberId", "PeriodKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_campaigns_ApplicationId_TriggerEventType_Status",
                schema: "campaigns",
                table: "campaigns",
                columns: new[] { "ApplicationId", "TriggerEventType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_event_log_ApplicationId_CreatedAt",
                schema: "notifications",
                table: "event_log",
                columns: new[] { "ApplicationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_event_log_ApplicationId_DeliveredAt",
                schema: "notifications",
                table: "event_log",
                columns: new[] { "ApplicationId", "DeliveredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_records_ApplicationId_AccountId_OperationName_I~",
                schema: "integration",
                table: "idempotency_records",
                columns: new[] { "ApplicationId", "AccountId", "OperationName", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ingestion_requests_ApplicationId_EventType_BusinessEventKey",
                schema: "integration",
                table: "ingestion_requests",
                columns: new[] { "ApplicationId", "EventType", "BusinessEventKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ingestion_requests_ApplicationId_SourceSystem_EventId",
                schema: "integration",
                table: "ingestion_requests",
                columns: new[] { "ApplicationId", "SourceSystem", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_member_segment_memberships_ApplicationId_MemberId_SegmentId~",
                schema: "cvm",
                table: "member_segment_memberships",
                columns: new[] { "ApplicationId", "MemberId", "SegmentId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_members_ApplicationId_ExternalCustomerId",
                schema: "members",
                table: "members",
                columns: new[] { "ApplicationId", "ExternalCustomerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_points_ledger_accounts_ApplicationId_AccountCode",
                schema: "wallets",
                table: "points_ledger_accounts",
                columns: new[] { "ApplicationId", "AccountCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_points_ledger_postings_LedgerTransactionId",
                schema: "wallets",
                table: "points_ledger_postings",
                column: "LedgerTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_points_ledger_transactions_ApplicationId_WalletId_Transacti~",
                schema: "wallets",
                table: "points_ledger_transactions",
                columns: new[] { "ApplicationId", "WalletId", "TransactionType", "SourceSystem", "BusinessEventKey", "EffectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reward_decisions_ApplicationId_MemberId_CreatedAt",
                schema: "campaigns",
                table: "reward_decisions",
                columns: new[] { "ApplicationId", "MemberId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_reward_effects_ApplicationId_MemberId_SourceEventId_Campaig~",
                schema: "integration",
                table: "reward_effects",
                columns: new[] { "ApplicationId", "MemberId", "SourceEventId", "CampaignId", "CampaignVersion", "EffectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_segment_definitions_ApplicationId_Name",
                schema: "cvm",
                table: "segment_definitions",
                columns: new[] { "ApplicationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_segment_versions_ApplicationId_SegmentId_Version",
                schema: "cvm",
                table: "segment_versions",
                columns: new[] { "ApplicationId", "SegmentId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wallets_ApplicationId_MemberId_WalletType",
                schema: "wallets",
                table: "wallets",
                columns: new[] { "ApplicationId", "MemberId", "WalletType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounts",
                schema: "accounts");

            migrationBuilder.DropTable(
                name: "application_closure",
                schema: "applications");

            migrationBuilder.DropTable(
                name: "applications",
                schema: "applications");

            migrationBuilder.DropTable(
                name: "campaign_usage",
                schema: "campaigns");

            migrationBuilder.DropTable(
                name: "campaigns",
                schema: "campaigns");

            migrationBuilder.DropTable(
                name: "cvm_settings",
                schema: "cvm");

            migrationBuilder.DropTable(
                name: "event_log",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "idempotency_records",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "ingestion_requests",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "loyalty_settings",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "member_segment_memberships",
                schema: "cvm");

            migrationBuilder.DropTable(
                name: "member_value_profiles",
                schema: "cvm");

            migrationBuilder.DropTable(
                name: "members",
                schema: "members");

            migrationBuilder.DropTable(
                name: "points_ledger_accounts",
                schema: "wallets");

            migrationBuilder.DropTable(
                name: "points_ledger_postings",
                schema: "wallets");

            migrationBuilder.DropTable(
                name: "reward_decisions",
                schema: "campaigns");

            migrationBuilder.DropTable(
                name: "reward_effects",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "segment_definitions",
                schema: "cvm");

            migrationBuilder.DropTable(
                name: "segment_versions",
                schema: "cvm");

            migrationBuilder.DropTable(
                name: "wallets",
                schema: "wallets");

            migrationBuilder.DropTable(
                name: "webhook_subscriptions",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "points_ledger_transactions",
                schema: "wallets");
        }
    }
}
