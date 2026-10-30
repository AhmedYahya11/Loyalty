using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Loyalty.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpiryAndStampConditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Conditions",
                schema: "engagement",
                table: "stamp_cards",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EndsAtUtc",
                schema: "engagement",
                table: "stamp_cards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartsAtUtc",
                schema: "engagement",
                table: "stamp_cards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PointsExpiryDays",
                schema: "settings",
                table: "loyalty_settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Conditions",
                schema: "engagement",
                table: "stamp_cards");

            migrationBuilder.DropColumn(
                name: "EndsAtUtc",
                schema: "engagement",
                table: "stamp_cards");

            migrationBuilder.DropColumn(
                name: "StartsAtUtc",
                schema: "engagement",
                table: "stamp_cards");

            migrationBuilder.DropColumn(
                name: "PointsExpiryDays",
                schema: "settings",
                table: "loyalty_settings");
        }
    }
}
