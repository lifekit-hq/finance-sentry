using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Wealth.Migrations
{
    /// <inheritdoc />
    public partial class M004_AddNetWorthSnapshotCashSplit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "brokerage_invested",
                table: "net_worth_snapshots",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cash_total",
                table: "net_worth_snapshots",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "crypto_invested",
                table: "net_worth_snapshots",
                type: "numeric(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "brokerage_invested",
                table: "net_worth_snapshots");

            migrationBuilder.DropColumn(
                name: "cash_total",
                table: "net_worth_snapshots");

            migrationBuilder.DropColumn(
                name: "crypto_invested",
                table: "net_worth_snapshots");
        }
    }
}
