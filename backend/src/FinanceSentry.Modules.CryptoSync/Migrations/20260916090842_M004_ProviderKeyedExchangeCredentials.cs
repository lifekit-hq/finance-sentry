using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.CryptoSync.Migrations
{
    /// <summary>
    /// Opens the crypto module to a second venue (#472, Revolut X).
    ///
    /// <list type="bullet">
    /// <item><b>Credentials become provider-keyed.</b> <c>BinanceCredentials</c> (unique on
    /// <c>UserId</c>) is replaced by <c>ExchangeCredentials</c> (unique on <c>UserId, Provider</c>).
    /// Every Binance row is copied across byte-for-byte — same ciphertext, IV, tag and key version —
    /// so nothing is decrypted or re-keyed here, and key rotation (#493) picks the rows up from the
    /// new table.</item>
    /// <item><b>Holdings are unique per venue.</b> <c>(UserId, Asset)</c> widens to
    /// <c>(UserId, Provider, Asset)</c>, so BTC on Binance and BTC on Revolut X are two rows. Every
    /// existing holding is Binance's; the backfill states that rather than trusting a column
    /// default, and the default is then dropped — a provider-agnostic table has no default
    /// venue.</item>
    /// <item><b>The trade cursor becomes opaque.</b> <c>LastTradeId</c> was a Binance trade id and
    /// was re-read inclusively, so the last fill was counted again on every run. It becomes
    /// <c>TradeCursor</c> holding the NEXT id to read (<c>LastTradeId + 1</c>), which the Binance
    /// adapter still reads as a bare number.</item>
    /// </list>
    /// </summary>
    public partial class M004_ProviderKeyedExchangeCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExchangeCredentials",
                schema: "crypto_sync",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EncryptedApiKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    ApiKeyIv = table.Column<byte[]>(type: "bytea", nullable: false),
                    ApiKeyAuthTag = table.Column<byte[]>(type: "bytea", nullable: false),
                    EncryptedApiSecret = table.Column<byte[]>(type: "bytea", nullable: false),
                    ApiSecretIv = table.Column<byte[]>(type: "bytea", nullable: false),
                    ApiSecretAuthTag = table.Column<byte[]>(type: "bytea", nullable: false),
                    KeyVersion = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastSyncAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSyncError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeCredentials", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeCredentials_UserId_Provider",
                schema: "crypto_sync",
                table: "ExchangeCredentials",
                columns: new[] { "UserId", "Provider" },
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO crypto_sync."ExchangeCredentials"
                    ("Id", "UserId", "Provider",
                     "EncryptedApiKey", "ApiKeyIv", "ApiKeyAuthTag",
                     "EncryptedApiSecret", "ApiSecretIv", "ApiSecretAuthTag",
                     "KeyVersion", "IsActive", "LastSyncAt", "LastSyncError", "CreatedAt")
                SELECT "Id", "UserId", 'binance',
                       "EncryptedApiKey", "ApiKeyIv", "ApiKeyAuthTag",
                       "EncryptedApiSecret", "ApiSecretIv", "ApiSecretAuthTag",
                       "KeyVersion", "IsActive", "LastSyncAt", "LastSyncError", "CreatedAt"
                FROM crypto_sync."BinanceCredentials";
                """);

            migrationBuilder.DropTable(
                name: "BinanceCredentials",
                schema: "crypto_sync");

            migrationBuilder.AddColumn<string>(
                name: "TradeCursor",
                schema: "crypto_sync",
                table: "CryptoHoldings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE crypto_sync."CryptoHoldings"
                SET "TradeCursor" = ("LastTradeId" + 1)::text
                WHERE "LastTradeId" > 0;
                """);

            migrationBuilder.DropColumn(
                name: "LastTradeId",
                schema: "crypto_sync",
                table: "CryptoHoldings");

            // Every holding written before this migration came from Binance.
            migrationBuilder.Sql("""
                UPDATE crypto_sync."CryptoHoldings"
                SET "Provider" = 'binance'
                WHERE "Provider" IS NULL OR "Provider" = '';
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Provider",
                schema: "crypto_sync",
                table: "CryptoHoldings",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "binance");

            migrationBuilder.DropIndex(
                name: "IX_CryptoHoldings_UserId_Asset",
                schema: "crypto_sync",
                table: "CryptoHoldings");

            migrationBuilder.CreateIndex(
                name: "IX_CryptoHoldings_UserId_Provider_Asset",
                schema: "crypto_sync",
                table: "CryptoHoldings",
                columns: new[] { "UserId", "Provider", "Asset" },
                unique: true);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Lossy by necessity: the old shape can hold one venue, so every non-Binance credential and
        /// holding is dropped, and a per-pair Binance cursor collapses to "resume from the latest
        /// fills" (<c>LastTradeId = 0</c>).
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM crypto_sync."CryptoHoldings" WHERE "Provider" <> 'binance';
                """);

            migrationBuilder.DropIndex(
                name: "IX_CryptoHoldings_UserId_Provider_Asset",
                schema: "crypto_sync",
                table: "CryptoHoldings");

            migrationBuilder.CreateIndex(
                name: "IX_CryptoHoldings_UserId_Asset",
                schema: "crypto_sync",
                table: "CryptoHoldings",
                columns: new[] { "UserId", "Asset" },
                unique: true);

            migrationBuilder.AlterColumn<string>(
                name: "Provider",
                schema: "crypto_sync",
                table: "CryptoHoldings",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "binance",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<long>(
                name: "LastTradeId",
                schema: "crypto_sync",
                table: "CryptoHoldings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql("""
                UPDATE crypto_sync."CryptoHoldings"
                SET "LastTradeId" = "TradeCursor"::bigint - 1
                WHERE "TradeCursor" ~ '^[0-9]+$' AND "TradeCursor"::bigint > 0;
                """);

            migrationBuilder.DropColumn(
                name: "TradeCursor",
                schema: "crypto_sync",
                table: "CryptoHoldings");

            migrationBuilder.CreateTable(
                name: "BinanceCredentials",
                schema: "crypto_sync",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApiKeyAuthTag = table.Column<byte[]>(type: "bytea", nullable: false),
                    ApiKeyIv = table.Column<byte[]>(type: "bytea", nullable: false),
                    ApiSecretAuthTag = table.Column<byte[]>(type: "bytea", nullable: false),
                    ApiSecretIv = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EncryptedApiKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    EncryptedApiSecret = table.Column<byte[]>(type: "bytea", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    KeyVersion = table.Column<int>(type: "integer", nullable: false),
                    LastSyncAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSyncError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BinanceCredentials", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BinanceCredentials_UserId",
                schema: "crypto_sync",
                table: "BinanceCredentials",
                column: "UserId",
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO crypto_sync."BinanceCredentials"
                    ("Id", "UserId",
                     "EncryptedApiKey", "ApiKeyIv", "ApiKeyAuthTag",
                     "EncryptedApiSecret", "ApiSecretIv", "ApiSecretAuthTag",
                     "KeyVersion", "IsActive", "LastSyncAt", "LastSyncError", "CreatedAt")
                SELECT "Id", "UserId",
                       "EncryptedApiKey", "ApiKeyIv", "ApiKeyAuthTag",
                       "EncryptedApiSecret", "ApiSecretIv", "ApiSecretAuthTag",
                       "KeyVersion", "IsActive", "LastSyncAt", "LastSyncError", "CreatedAt"
                FROM crypto_sync."ExchangeCredentials"
                WHERE "Provider" = 'binance';
                """);

            migrationBuilder.DropTable(
                name: "ExchangeCredentials",
                schema: "crypto_sync");
        }
    }
}
