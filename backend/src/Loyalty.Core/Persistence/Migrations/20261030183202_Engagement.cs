using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Loyalty.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Engagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "engagement");

            migrationBuilder.AddColumn<DateOnly>(
                name: "BirthDate",
                schema: "members",
                table: "members",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferralCode",
                schema: "members",
                table: "members",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "engagement_settings",
                schema: "engagement",
                columns: table => new
                {
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferralEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ReferrerPoints = table.Column<long>(type: "bigint", nullable: false),
                    RefereePoints = table.Column<long>(type: "bigint", nullable: false),
                    BirthdayEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    BirthdayBonusPoints = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_engagement_settings", x => x.ApplicationId);
                });

            migrationBuilder.CreateTable(
                name: "member_stamp_cards",
                schema: "engagement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    StampCardId = table.Column<Guid>(type: "uuid", nullable: false),
                    Stamps = table.Column<int>(type: "integer", nullable: false),
                    CompletedCount = table.Column<int>(type: "integer", nullable: false),
                    LastStampedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_member_stamp_cards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "referrals",
                schema: "engagement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferralKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReferrerMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    RefereeMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferrerPointsAwarded = table.Column<long>(type: "bigint", nullable: false),
                    RefereePointsAwarded = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_referrals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stamp_cards",
                schema: "engagement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TriggerEventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StampsRequired = table.Column<int>(type: "integer", nullable: false),
                    RewardPoints = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stamp_cards", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_members_ApplicationId_ReferralCode",
                schema: "members",
                table: "members",
                columns: new[] { "ApplicationId", "ReferralCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_member_stamp_cards_ApplicationId_MemberId_StampCardId",
                schema: "engagement",
                table: "member_stamp_cards",
                columns: new[] { "ApplicationId", "MemberId", "StampCardId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_referrals_ApplicationId_ReferralKey",
                schema: "engagement",
                table: "referrals",
                columns: new[] { "ApplicationId", "ReferralKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_referrals_ApplicationId_ReferrerMemberId",
                schema: "engagement",
                table: "referrals",
                columns: new[] { "ApplicationId", "ReferrerMemberId" });

            migrationBuilder.CreateIndex(
                name: "IX_stamp_cards_ApplicationId_IsActive",
                schema: "engagement",
                table: "stamp_cards",
                columns: new[] { "ApplicationId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "engagement_settings",
                schema: "engagement");

            migrationBuilder.DropTable(
                name: "member_stamp_cards",
                schema: "engagement");

            migrationBuilder.DropTable(
                name: "referrals",
                schema: "engagement");

            migrationBuilder.DropTable(
                name: "stamp_cards",
                schema: "engagement");

            migrationBuilder.DropIndex(
                name: "IX_members_ApplicationId_ReferralCode",
                schema: "members",
                table: "members");

            migrationBuilder.DropColumn(
                name: "BirthDate",
                schema: "members",
                table: "members");

            migrationBuilder.DropColumn(
                name: "ReferralCode",
                schema: "members",
                table: "members");
        }
    }
}
