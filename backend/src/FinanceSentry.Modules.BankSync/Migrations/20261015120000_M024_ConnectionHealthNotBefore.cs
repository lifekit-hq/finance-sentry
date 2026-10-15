using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.BankSync.Migrations
{
    /// <inheritdoc />
    public partial class M024_ConnectionHealthNotBefore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_NotBefore",
                schema: "bank_sync",
                table: "BankAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_NotBefore",
                schema: "bank_sync",
                table: "MonobankCredentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_NotBefore",
                schema: "bank_sync",
                table: "TrueLayerConnections",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Health_NotBefore",
                schema: "bank_sync",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "Health_NotBefore",
                schema: "bank_sync",
                table: "MonobankCredentials");

            migrationBuilder.DropColumn(
                name: "Health_NotBefore",
                schema: "bank_sync",
                table: "TrueLayerConnections");
        }
    }
}
