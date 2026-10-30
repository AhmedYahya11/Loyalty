using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Loyalty.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Raffles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "raffles");

            migrationBuilder.CreateTable(
                name: "raffle_entries",
                schema: "raffles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RaffleId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_raffle_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "raffles",
                schema: "raffles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TriggerEventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Conditions = table.Column<string>(type: "jsonb", nullable: false),
                    MinAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    EntriesPerEvent = table.Column<int>(type: "integer", nullable: false),
                    PrizePoints = table.Column<long>(type: "bigint", nullable: false),
                    PrizeDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DrawAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    WinnerMemberId = table.Column<Guid>(type: "uuid", nullable: true),
                    DrawnAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_raffles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_raffle_entries_ApplicationId_RaffleId_SourceEventId",
                schema: "raffles",
                table: "raffle_entries",
                columns: new[] { "ApplicationId", "RaffleId", "SourceEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_raffle_entries_RaffleId_MemberId",
                schema: "raffles",
                table: "raffle_entries",
                columns: new[] { "RaffleId", "MemberId" });

            migrationBuilder.CreateIndex(
                name: "IX_raffles_ApplicationId_Status",
                schema: "raffles",
                table: "raffles",
                columns: new[] { "ApplicationId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "raffle_entries",
                schema: "raffles");

            migrationBuilder.DropTable(
                name: "raffles",
                schema: "raffles");
        }
    }
}
