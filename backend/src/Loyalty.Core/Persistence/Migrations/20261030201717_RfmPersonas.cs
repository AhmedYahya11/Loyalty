using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Loyalty.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RfmPersonas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RfmEvaluatedAt",
                schema: "cvm",
                table: "member_value_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RfmFrequency",
                schema: "cvm",
                table: "member_value_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RfmMonetary",
                schema: "cvm",
                table: "member_value_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RfmPersona",
                schema: "cvm",
                table: "member_value_profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RfmRecency",
                schema: "cvm",
                table: "member_value_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RfmEvaluatedAt",
                schema: "cvm",
                table: "member_value_profiles");

            migrationBuilder.DropColumn(
                name: "RfmFrequency",
                schema: "cvm",
                table: "member_value_profiles");

            migrationBuilder.DropColumn(
                name: "RfmMonetary",
                schema: "cvm",
                table: "member_value_profiles");

            migrationBuilder.DropColumn(
                name: "RfmPersona",
                schema: "cvm",
                table: "member_value_profiles");

            migrationBuilder.DropColumn(
                name: "RfmRecency",
                schema: "cvm",
                table: "member_value_profiles");
        }
    }
}
