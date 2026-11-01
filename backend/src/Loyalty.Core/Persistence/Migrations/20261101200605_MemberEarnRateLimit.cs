using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Loyalty.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MemberEarnRateLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxEarnEventsPerHour",
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
                name: "MaxEarnEventsPerHour",
                schema: "settings",
                table: "loyalty_settings");
        }
    }
}
