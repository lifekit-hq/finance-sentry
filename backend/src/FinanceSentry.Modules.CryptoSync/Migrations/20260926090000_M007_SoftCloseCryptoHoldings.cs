using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.CryptoSync.Migrations
{
    /// <summary>
    /// Soft-close crypto holdings (#435 S2): a position that leaves the venue is marked closed
    /// instead of deleted, keeping its trade cursor and realized totals. Nullable, so every
    /// existing row stays open.
    /// </summary>
    public partial class M007_SoftCloseCryptoHoldings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAt",
                schema: "crypto_sync",
                table: "CryptoHoldings",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClosedAt",
                schema: "crypto_sync",
                table: "CryptoHoldings");
        }
    }
}
