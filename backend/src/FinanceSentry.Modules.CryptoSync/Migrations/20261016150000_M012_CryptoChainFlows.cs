using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.CryptoSync.Migrations
{
    /// <inheritdoc />
    public partial class M012_CryptoChainFlows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ChainFlowsWalkedTo",
                schema: "crypto_sync",
                table: "ExchangeCredentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Network",
                schema: "crypto_sync",
                table: "CryptoFlowEvents",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TxRef",
                schema: "crypto_sync",
                table: "CryptoFlowEvents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChainFlowsWalkedTo",
                schema: "crypto_sync",
                table: "ExchangeCredentials");

            migrationBuilder.DropColumn(
                name: "Network",
                schema: "crypto_sync",
                table: "CryptoFlowEvents");

            migrationBuilder.DropColumn(
                name: "TxRef",
                schema: "crypto_sync",
                table: "CryptoFlowEvents");
        }
    }
}
