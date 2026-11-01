using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Loyalty.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Branding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "branding");

            migrationBuilder.CreateTable(
                name: "branding",
                schema: "branding",
                columns: table => new
                {
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BrandName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    LogoUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    PrimaryColor = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    AccentColor = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branding", x => x.ApplicationId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "branding",
                schema: "branding");
        }
    }
}
