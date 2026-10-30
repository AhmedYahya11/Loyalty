using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Loyalty.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Tiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tiers");

            migrationBuilder.AddColumn<Guid>(
                name: "CurrentTierId",
                schema: "cvm",
                table: "member_value_profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrentTierRank",
                schema: "cvm",
                table: "member_value_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TierEvaluatedAt",
                schema: "cvm",
                table: "member_value_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tier_history",
                schema: "tiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromTierId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromRank = table.Column<int>(type: "integer", nullable: false),
                    ToTierId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToRank = table.Column<int>(type: "integer", nullable: false),
                    EvaluatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tier_history", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tiers",
                schema: "tiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    ThresholdMetric = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ThresholdValue = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    EarnMultiplier = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    Color = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tiers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tier_history_ApplicationId_MemberId_EvaluatedAt",
                schema: "tiers",
                table: "tier_history",
                columns: new[] { "ApplicationId", "MemberId", "EvaluatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_tiers_ApplicationId_Name",
                schema: "tiers",
                table: "tiers",
                columns: new[] { "ApplicationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tiers_ApplicationId_Rank",
                schema: "tiers",
                table: "tiers",
                columns: new[] { "ApplicationId", "Rank" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tier_history",
                schema: "tiers");

            migrationBuilder.DropTable(
                name: "tiers",
                schema: "tiers");

            migrationBuilder.DropColumn(
                name: "CurrentTierId",
                schema: "cvm",
                table: "member_value_profiles");

            migrationBuilder.DropColumn(
                name: "CurrentTierRank",
                schema: "cvm",
                table: "member_value_profiles");

            migrationBuilder.DropColumn(
                name: "TierEvaluatedAt",
                schema: "cvm",
                table: "member_value_profiles");
        }
    }
}
