using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.BankSync.Migrations
{
    /// <inheritdoc />
    public partial class M025_CardLifecycleColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CardValidTo",
                schema: "bank_sync",
                table: "BankAccounts",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosedAt",
                schema: "bank_sync",
                table: "BankAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosedBy",
                schema: "bank_sync",
                table: "BankAccounts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosedReason",
                schema: "bank_sync",
                table: "BankAccounts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Iban",
                schema: "bank_sync",
                table: "BankAccounts",
                type: "character varying(34)",
                maxLength: 34,
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "MaskedPans",
                schema: "bank_sync",
                table: "BankAccounts",
                type: "text[]",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PredecessorAccountId",
                schema: "bank_sync",
                table: "BankAccounts",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CardValidTo",
                schema: "bank_sync",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                schema: "bank_sync",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "ClosedBy",
                schema: "bank_sync",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "ClosedReason",
                schema: "bank_sync",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "Iban",
                schema: "bank_sync",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "MaskedPans",
                schema: "bank_sync",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "PredecessorAccountId",
                schema: "bank_sync",
                table: "BankAccounts");
        }
    }
}
