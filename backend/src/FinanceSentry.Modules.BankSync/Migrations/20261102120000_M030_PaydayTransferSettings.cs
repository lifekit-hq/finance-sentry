using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.BankSync.Migrations
{
    /// <summary>
    /// The per-user inputs of the payday-transfer figure (#1272): the fixed monthly outflow and the EUR rounding step.
    /// Empty on creation - a user without a row reads the defaults (15000 UAH, 50 EUR).
    /// </summary>
    [DbContext(typeof(BankSyncDbContext))]
    [Migration("20261102120000_M030_PaydayTransferSettings")]
    public partial class M030_PaydayTransferSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payday_transfer_settings",
                schema: "bank_sync",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FixedOutflowUah = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    RoundingStepEur = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payday_transfer_settings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_payday_transfer_settings_user_unique",
                schema: "bank_sync",
                table: "payday_transfer_settings",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payday_transfer_settings",
                schema: "bank_sync");
        }
    }
}
