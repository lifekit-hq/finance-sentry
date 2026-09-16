using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.CryptoSync.Migrations
{
    /// <summary>
    /// Revolut X trade ingestion and venue fiat (#472, second PR). Additive only.
    ///
    /// <list type="bullet">
    /// <item><b><c>IsFiat</c></b> flags fiat cash held on a venue; every existing row is crypto, so
    /// the backfill is <c>false</c>.</item>
    /// <item><b><c>TrackedQuantity</c> / <c>TrackedCostUsd</c> / <c>UntrackedQuantity</c></b> are the
    /// forward cost-basis ledger for venues whose fills start at connect. Null means the ledger has
    /// not run, so an existing Revolut X row starts its walk from the connect date on the next sync;
    /// Binance rows never use them.</item>
    /// </list>
    /// </summary>
    public partial class M005_VenueFiatAndForwardLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFiat",
                schema: "crypto_sync",
                table: "CryptoHoldings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "TrackedCostUsd",
                schema: "crypto_sync",
                table: "CryptoHoldings",
                type: "numeric(30,10)",
                precision: 30,
                scale: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TrackedQuantity",
                schema: "crypto_sync",
                table: "CryptoHoldings",
                type: "numeric(30,10)",
                precision: 30,
                scale: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UntrackedQuantity",
                schema: "crypto_sync",
                table: "CryptoHoldings",
                type: "numeric(30,10)",
                precision: 30,
                scale: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsFiat",
                schema: "crypto_sync",
                table: "CryptoHoldings");

            migrationBuilder.DropColumn(
                name: "TrackedCostUsd",
                schema: "crypto_sync",
                table: "CryptoHoldings");

            migrationBuilder.DropColumn(
                name: "TrackedQuantity",
                schema: "crypto_sync",
                table: "CryptoHoldings");

            migrationBuilder.DropColumn(
                name: "UntrackedQuantity",
                schema: "crypto_sync",
                table: "CryptoHoldings");
        }
    }
}
