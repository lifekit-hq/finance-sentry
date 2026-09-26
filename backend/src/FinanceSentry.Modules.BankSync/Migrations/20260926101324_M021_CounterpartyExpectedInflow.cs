using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.BankSync.Migrations
{
    /// <inheritdoc />
    public partial class M021_CounterpartyExpectedInflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedMonthlyInflowAmount",
                schema: "bank_sync",
                table: "counterparties",
                type: "numeric(15,2)",
                precision: 15,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedMonthlyInflowCurrency",
                schema: "bank_sync",
                table: "counterparties",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpectedMonthlyInflowAmount",
                schema: "bank_sync",
                table: "counterparties");

            migrationBuilder.DropColumn(
                name: "ExpectedMonthlyInflowCurrency",
                schema: "bank_sync",
                table: "counterparties");
        }
    }
}
