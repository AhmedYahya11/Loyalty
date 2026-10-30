using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Loyalty.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RewardsRedemptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "rewards");

            migrationBuilder.CreateTable(
                name: "redemptions",
                schema: "rewards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    RewardId = table.Column<Guid>(type: "uuid", nullable: false),
                    PointsCost = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CouponCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_redemptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rewards",
                schema: "rewards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PointsCost = table.Column<long>(type: "bigint", nullable: false),
                    RewardType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Inventory = table.Column<int>(type: "integer", nullable: true),
                    PerMemberLimit = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rewards", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_redemptions_ApplicationId_CouponCode",
                schema: "rewards",
                table: "redemptions",
                columns: new[] { "ApplicationId", "CouponCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_redemptions_ApplicationId_MemberId_Status",
                schema: "rewards",
                table: "redemptions",
                columns: new[] { "ApplicationId", "MemberId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_rewards_ApplicationId_IsActive",
                schema: "rewards",
                table: "rewards",
                columns: new[] { "ApplicationId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "redemptions",
                schema: "rewards");

            migrationBuilder.DropTable(
                name: "rewards",
                schema: "rewards");
        }
    }
}
