using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.CryptoSync.Migrations
{
    /// <inheritdoc />
    public partial class M013_CryptoTradeFee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Fee",
                schema: "crypto_sync",
                table: "CryptoTrades",
                type: "numeric(30,10)",
                precision: 30,
                scale: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FeeAsset",
                schema: "crypto_sync",
                table: "CryptoTrades",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FeeUsd",
                schema: "crypto_sync",
                table: "CryptoTrades",
                type: "numeric(20,4)",
                precision: 20,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Fee", schema: "crypto_sync", table: "CryptoTrades");
            migrationBuilder.DropColumn(name: "FeeAsset", schema: "crypto_sync", table: "CryptoTrades");
            migrationBuilder.DropColumn(name: "FeeUsd", schema: "crypto_sync", table: "CryptoTrades");
        }
    }
}
