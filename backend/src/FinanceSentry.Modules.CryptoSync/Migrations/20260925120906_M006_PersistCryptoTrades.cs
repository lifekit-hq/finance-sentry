using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.CryptoSync.Migrations
{
    /// <summary>
    /// Persist crypto fills instead of discarding them after the cost-basis walk (#435 S1). New
    /// <c>CryptoTrades</c> table, independent of <c>CryptoHoldings</c> — no foreign key, so a
    /// holding row closing or being reconciled away never deletes the fills that landed under it.
    /// Idempotent on <c>(UserId, Provider, TradeId)</c>, enforced by the unique index.
    /// </summary>
    public partial class M006_PersistCryptoTrades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CryptoTrades",
                schema: "crypto_sync",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TradeId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Asset = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    QuoteAsset = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(30,10)", precision: 30, scale: 10, nullable: false),
                    PriceUsd = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    QuoteQuantityUsd = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    IsBuyer = table.Column<bool>(type: "boolean", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CryptoTrades", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CryptoTrades_UserId_Provider_TradeId",
                schema: "crypto_sync",
                table: "CryptoTrades",
                columns: new[] { "UserId", "Provider", "TradeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CryptoTrades",
                schema: "crypto_sync");
        }
    }
}
