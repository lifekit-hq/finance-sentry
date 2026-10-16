using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.CryptoSync.Migrations
{
    /// <inheritdoc />
    public partial class M011_CryptoFirstLinkedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FirstLinkedAt",
                schema: "crypto_sync",
                table: "ExchangeCredentials",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "FlowsWalkLane",
                schema: "crypto_sync",
                table: "ExchangeCredentials",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // CreatedAt is the earliest link time still on record (it carries any past reconnect).
            migrationBuilder.Sql("""
                UPDATE crypto_sync."ExchangeCredentials" SET "FirstLinkedAt" = "CreatedAt";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirstLinkedAt",
                schema: "crypto_sync",
                table: "ExchangeCredentials");

            migrationBuilder.DropColumn(
                name: "FlowsWalkLane",
                schema: "crypto_sync",
                table: "ExchangeCredentials");
        }
    }
}
