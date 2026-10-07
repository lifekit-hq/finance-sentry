using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.CryptoSync.Migrations
{
    /// <inheritdoc />
    public partial class M008_ConnectionHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Health_ConsecutiveFailures",
                schema: "crypto_sync",
                table: "ExchangeCredentials",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_FirstFailureAt",
                schema: "crypto_sync",
                table: "ExchangeCredentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_LastFailureAt",
                schema: "crypto_sync",
                table: "ExchangeCredentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Health_LastFailureClass",
                schema: "crypto_sync",
                table: "ExchangeCredentials",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Health_LastFailureCode",
                schema: "crypto_sync",
                table: "ExchangeCredentials",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_LastSuccessAt",
                schema: "crypto_sync",
                table: "ExchangeCredentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Health_State",
                schema: "crypto_sync",
                table: "ExchangeCredentials",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Healthy");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_StateChangedAt",
                schema: "crypto_sync",
                table: "ExchangeCredentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_SuspectSince",
                schema: "crypto_sync",
                table: "ExchangeCredentials",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Health_ConsecutiveFailures",
                schema: "crypto_sync",
                table: "ExchangeCredentials");

            migrationBuilder.DropColumn(
                name: "Health_FirstFailureAt",
                schema: "crypto_sync",
                table: "ExchangeCredentials");

            migrationBuilder.DropColumn(
                name: "Health_LastFailureAt",
                schema: "crypto_sync",
                table: "ExchangeCredentials");

            migrationBuilder.DropColumn(
                name: "Health_LastFailureClass",
                schema: "crypto_sync",
                table: "ExchangeCredentials");

            migrationBuilder.DropColumn(
                name: "Health_LastFailureCode",
                schema: "crypto_sync",
                table: "ExchangeCredentials");

            migrationBuilder.DropColumn(
                name: "Health_LastSuccessAt",
                schema: "crypto_sync",
                table: "ExchangeCredentials");

            migrationBuilder.DropColumn(
                name: "Health_State",
                schema: "crypto_sync",
                table: "ExchangeCredentials");

            migrationBuilder.DropColumn(
                name: "Health_StateChangedAt",
                schema: "crypto_sync",
                table: "ExchangeCredentials");

            migrationBuilder.DropColumn(
                name: "Health_SuspectSince",
                schema: "crypto_sync",
                table: "ExchangeCredentials");
        }
    }
}
