using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.BankSync.Migrations
{
    /// <inheritdoc />
    public partial class M022_ConnectionHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Health_ConsecutiveFailures",
                schema: "bank_sync",
                table: "TrueLayerConnections",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_FirstFailureAt",
                schema: "bank_sync",
                table: "TrueLayerConnections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_LastFailureAt",
                schema: "bank_sync",
                table: "TrueLayerConnections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Health_LastFailureClass",
                schema: "bank_sync",
                table: "TrueLayerConnections",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Health_LastFailureCode",
                schema: "bank_sync",
                table: "TrueLayerConnections",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_LastSuccessAt",
                schema: "bank_sync",
                table: "TrueLayerConnections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Health_State",
                schema: "bank_sync",
                table: "TrueLayerConnections",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Healthy");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_StateChangedAt",
                schema: "bank_sync",
                table: "TrueLayerConnections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_SuspectSince",
                schema: "bank_sync",
                table: "TrueLayerConnections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Health_ConsecutiveFailures",
                schema: "bank_sync",
                table: "MonobankCredentials",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_FirstFailureAt",
                schema: "bank_sync",
                table: "MonobankCredentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_LastFailureAt",
                schema: "bank_sync",
                table: "MonobankCredentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Health_LastFailureClass",
                schema: "bank_sync",
                table: "MonobankCredentials",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Health_LastFailureCode",
                schema: "bank_sync",
                table: "MonobankCredentials",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_LastSuccessAt",
                schema: "bank_sync",
                table: "MonobankCredentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Health_State",
                schema: "bank_sync",
                table: "MonobankCredentials",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Healthy");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_StateChangedAt",
                schema: "bank_sync",
                table: "MonobankCredentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_SuspectSince",
                schema: "bank_sync",
                table: "MonobankCredentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Health_ConsecutiveFailures",
                schema: "bank_sync",
                table: "BankAccounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_FirstFailureAt",
                schema: "bank_sync",
                table: "BankAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_LastFailureAt",
                schema: "bank_sync",
                table: "BankAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Health_LastFailureClass",
                schema: "bank_sync",
                table: "BankAccounts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Health_LastFailureCode",
                schema: "bank_sync",
                table: "BankAccounts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_LastSuccessAt",
                schema: "bank_sync",
                table: "BankAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Health_State",
                schema: "bank_sync",
                table: "BankAccounts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Healthy");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_StateChangedAt",
                schema: "bank_sync",
                table: "BankAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_SuspectSince",
                schema: "bank_sync",
                table: "BankAccounts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Health_ConsecutiveFailures",
                schema: "bank_sync",
                table: "TrueLayerConnections");

            migrationBuilder.DropColumn(
                name: "Health_FirstFailureAt",
                schema: "bank_sync",
                table: "TrueLayerConnections");

            migrationBuilder.DropColumn(
                name: "Health_LastFailureAt",
                schema: "bank_sync",
                table: "TrueLayerConnections");

            migrationBuilder.DropColumn(
                name: "Health_LastFailureClass",
                schema: "bank_sync",
                table: "TrueLayerConnections");

            migrationBuilder.DropColumn(
                name: "Health_LastFailureCode",
                schema: "bank_sync",
                table: "TrueLayerConnections");

            migrationBuilder.DropColumn(
                name: "Health_LastSuccessAt",
                schema: "bank_sync",
                table: "TrueLayerConnections");

            migrationBuilder.DropColumn(
                name: "Health_State",
                schema: "bank_sync",
                table: "TrueLayerConnections");

            migrationBuilder.DropColumn(
                name: "Health_StateChangedAt",
                schema: "bank_sync",
                table: "TrueLayerConnections");

            migrationBuilder.DropColumn(
                name: "Health_SuspectSince",
                schema: "bank_sync",
                table: "TrueLayerConnections");

            migrationBuilder.DropColumn(
                name: "Health_ConsecutiveFailures",
                schema: "bank_sync",
                table: "MonobankCredentials");

            migrationBuilder.DropColumn(
                name: "Health_FirstFailureAt",
                schema: "bank_sync",
                table: "MonobankCredentials");

            migrationBuilder.DropColumn(
                name: "Health_LastFailureAt",
                schema: "bank_sync",
                table: "MonobankCredentials");

            migrationBuilder.DropColumn(
                name: "Health_LastFailureClass",
                schema: "bank_sync",
                table: "MonobankCredentials");

            migrationBuilder.DropColumn(
                name: "Health_LastFailureCode",
                schema: "bank_sync",
                table: "MonobankCredentials");

            migrationBuilder.DropColumn(
                name: "Health_LastSuccessAt",
                schema: "bank_sync",
                table: "MonobankCredentials");

            migrationBuilder.DropColumn(
                name: "Health_State",
                schema: "bank_sync",
                table: "MonobankCredentials");

            migrationBuilder.DropColumn(
                name: "Health_StateChangedAt",
                schema: "bank_sync",
                table: "MonobankCredentials");

            migrationBuilder.DropColumn(
                name: "Health_SuspectSince",
                schema: "bank_sync",
                table: "MonobankCredentials");

            migrationBuilder.DropColumn(
                name: "Health_ConsecutiveFailures",
                schema: "bank_sync",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "Health_FirstFailureAt",
                schema: "bank_sync",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "Health_LastFailureAt",
                schema: "bank_sync",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "Health_LastFailureClass",
                schema: "bank_sync",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "Health_LastFailureCode",
                schema: "bank_sync",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "Health_LastSuccessAt",
                schema: "bank_sync",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "Health_State",
                schema: "bank_sync",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "Health_StateChangedAt",
                schema: "bank_sync",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "Health_SuspectSince",
                schema: "bank_sync",
                table: "BankAccounts");
        }
    }
}
