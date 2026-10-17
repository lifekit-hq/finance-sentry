using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.BankSync.Migrations
{
    /// <inheritdoc />
    public partial class M027_AccountPreviousLast4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreviousLast4",
                schema: "bank_sync",
                table: "BankAccounts",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PanChangedAt",
                schema: "bank_sync",
                table: "BankAccounts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PanChangedAt",
                schema: "bank_sync",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "PreviousLast4",
                schema: "bank_sync",
                table: "BankAccounts");
        }
    }
}
