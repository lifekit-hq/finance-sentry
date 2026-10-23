using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.BankSync.Migrations
{
    /// <summary>
    /// The coverage sentinel's holes (#1238): periods in which an account's day-to-day spend stopped reaching us while
    /// its syncs kept succeeding. Empty on creation - the sentinel writes them from the next daily run, and no history
    /// is rewritten.
    /// </summary>
    [DbContext(typeof(BankSyncDbContext))]
    [Migration("20261023120000_M029_CoverageHoles")]
    public partial class M029_CoverageHoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "coverage_holes",
                schema: "bank_sync",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    From = table.Column<DateOnly>(type: "date", nullable: false),
                    To = table.Column<DateOnly>(type: "date", nullable: true),
                    BaselineWeeklySpendUsd = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coverage_holes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_coverage_hole_open_account_unique",
                schema: "bank_sync",
                table: "coverage_holes",
                column: "AccountId",
                unique: true,
                filter: "\"To\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "idx_coverage_hole_user_from",
                schema: "bank_sync",
                table: "coverage_holes",
                columns: ["UserId", "From"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "coverage_holes",
                schema: "bank_sync");
        }
    }
}
