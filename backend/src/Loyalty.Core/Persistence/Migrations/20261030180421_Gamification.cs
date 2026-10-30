using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Loyalty.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Gamification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "gamification");

            migrationBuilder.AddColumn<int>(
                name: "CurrentStreak",
                schema: "cvm",
                table: "member_value_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LastStreakDate",
                schema: "cvm",
                table: "member_value_profiles",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LongestStreak",
                schema: "cvm",
                table: "member_value_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "achievements",
                schema: "gamification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Icon = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Metric = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Threshold = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    BonusPoints = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_achievements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "challenges",
                schema: "gamification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Metric = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TargetValue = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BonusPoints = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_challenges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "member_achievements",
                schema: "gamification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    AchievementId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnlockedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_member_achievements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "member_challenge_progress",
                schema: "gamification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Progress = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Completed = table.Column<bool>(type: "boolean", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_member_challenge_progress", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_achievements_ApplicationId_IsActive",
                schema: "gamification",
                table: "achievements",
                columns: new[] { "ApplicationId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_challenges_ApplicationId_IsActive",
                schema: "gamification",
                table: "challenges",
                columns: new[] { "ApplicationId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_member_achievements_ApplicationId_MemberId_AchievementId",
                schema: "gamification",
                table: "member_achievements",
                columns: new[] { "ApplicationId", "MemberId", "AchievementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_member_challenge_progress_ApplicationId_MemberId_ChallengeId",
                schema: "gamification",
                table: "member_challenge_progress",
                columns: new[] { "ApplicationId", "MemberId", "ChallengeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "achievements",
                schema: "gamification");

            migrationBuilder.DropTable(
                name: "challenges",
                schema: "gamification");

            migrationBuilder.DropTable(
                name: "member_achievements",
                schema: "gamification");

            migrationBuilder.DropTable(
                name: "member_challenge_progress",
                schema: "gamification");

            migrationBuilder.DropColumn(
                name: "CurrentStreak",
                schema: "cvm",
                table: "member_value_profiles");

            migrationBuilder.DropColumn(
                name: "LastStreakDate",
                schema: "cvm",
                table: "member_value_profiles");

            migrationBuilder.DropColumn(
                name: "LongestStreak",
                schema: "cvm",
                table: "member_value_profiles");
        }
    }
}
