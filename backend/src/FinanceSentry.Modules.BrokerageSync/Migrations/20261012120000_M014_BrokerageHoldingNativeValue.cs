using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.BrokerageSync.Migrations
{
    /// <summary>
    /// A holding carries the currency it is denominated in and its amount in that currency, next to the USD figure
    /// converted at ingest. Existing rows are USD with no native amount; Inzhur rows (UAH) take theirs on the next
    /// daily sync, so no stored amount is converted or rewritten here.
    /// </summary>
    public partial class M014_BrokerageHoldingNativeValue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                schema: "brokerage_sync",
                table: "BrokerageHoldings",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "USD");

            migrationBuilder.AddColumn<decimal>(
                name: "NativeValue",
                schema: "brokerage_sync",
                table: "BrokerageHoldings",
                type: "numeric(20,4)",
                precision: 20,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Currency",
                schema: "brokerage_sync",
                table: "BrokerageHoldings");

            migrationBuilder.DropColumn(
                name: "NativeValue",
                schema: "brokerage_sync",
                table: "BrokerageHoldings");
        }
    }
}
