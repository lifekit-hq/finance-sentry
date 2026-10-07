using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.BrokerageSync.Migrations
{
    /// <inheritdoc />
    public partial class M011_ConnectionHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Health_ConsecutiveFailures",
                schema: "brokerage_sync",
                table: "IBKRFlexCredentials",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_FirstFailureAt",
                schema: "brokerage_sync",
                table: "IBKRFlexCredentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_LastFailureAt",
                schema: "brokerage_sync",
                table: "IBKRFlexCredentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Health_LastFailureClass",
                schema: "brokerage_sync",
                table: "IBKRFlexCredentials",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Health_LastFailureCode",
                schema: "brokerage_sync",
                table: "IBKRFlexCredentials",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_LastSuccessAt",
                schema: "brokerage_sync",
                table: "IBKRFlexCredentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Health_State",
                schema: "brokerage_sync",
                table: "IBKRFlexCredentials",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Healthy");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_StateChangedAt",
                schema: "brokerage_sync",
                table: "IBKRFlexCredentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_SuspectSince",
                schema: "brokerage_sync",
                table: "IBKRFlexCredentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Health_ConsecutiveFailures",
                schema: "brokerage_sync",
                table: "IBKRCredentials",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_FirstFailureAt",
                schema: "brokerage_sync",
                table: "IBKRCredentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_LastFailureAt",
                schema: "brokerage_sync",
                table: "IBKRCredentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Health_LastFailureClass",
                schema: "brokerage_sync",
                table: "IBKRCredentials",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Health_LastFailureCode",
                schema: "brokerage_sync",
                table: "IBKRCredentials",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_LastSuccessAt",
                schema: "brokerage_sync",
                table: "IBKRCredentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Health_State",
                schema: "brokerage_sync",
                table: "IBKRCredentials",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Healthy");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_StateChangedAt",
                schema: "brokerage_sync",
                table: "IBKRCredentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_SuspectSince",
                schema: "brokerage_sync",
                table: "IBKRCredentials",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Health_ConsecutiveFailures",
                schema: "brokerage_sync",
                table: "IBKRFlexCredentials");

            migrationBuilder.DropColumn(
                name: "Health_FirstFailureAt",
                schema: "brokerage_sync",
                table: "IBKRFlexCredentials");

            migrationBuilder.DropColumn(
                name: "Health_LastFailureAt",
                schema: "brokerage_sync",
                table: "IBKRFlexCredentials");

            migrationBuilder.DropColumn(
                name: "Health_LastFailureClass",
                schema: "brokerage_sync",
                table: "IBKRFlexCredentials");

            migrationBuilder.DropColumn(
                name: "Health_LastFailureCode",
                schema: "brokerage_sync",
                table: "IBKRFlexCredentials");

            migrationBuilder.DropColumn(
                name: "Health_LastSuccessAt",
                schema: "brokerage_sync",
                table: "IBKRFlexCredentials");

            migrationBuilder.DropColumn(
                name: "Health_State",
                schema: "brokerage_sync",
                table: "IBKRFlexCredentials");

            migrationBuilder.DropColumn(
                name: "Health_StateChangedAt",
                schema: "brokerage_sync",
                table: "IBKRFlexCredentials");

            migrationBuilder.DropColumn(
                name: "Health_SuspectSince",
                schema: "brokerage_sync",
                table: "IBKRFlexCredentials");

            migrationBuilder.DropColumn(
                name: "Health_ConsecutiveFailures",
                schema: "brokerage_sync",
                table: "IBKRCredentials");

            migrationBuilder.DropColumn(
                name: "Health_FirstFailureAt",
                schema: "brokerage_sync",
                table: "IBKRCredentials");

            migrationBuilder.DropColumn(
                name: "Health_LastFailureAt",
                schema: "brokerage_sync",
                table: "IBKRCredentials");

            migrationBuilder.DropColumn(
                name: "Health_LastFailureClass",
                schema: "brokerage_sync",
                table: "IBKRCredentials");

            migrationBuilder.DropColumn(
                name: "Health_LastFailureCode",
                schema: "brokerage_sync",
                table: "IBKRCredentials");

            migrationBuilder.DropColumn(
                name: "Health_LastSuccessAt",
                schema: "brokerage_sync",
                table: "IBKRCredentials");

            migrationBuilder.DropColumn(
                name: "Health_State",
                schema: "brokerage_sync",
                table: "IBKRCredentials");

            migrationBuilder.DropColumn(
                name: "Health_StateChangedAt",
                schema: "brokerage_sync",
                table: "IBKRCredentials");

            migrationBuilder.DropColumn(
                name: "Health_SuspectSince",
                schema: "brokerage_sync",
                table: "IBKRCredentials");
        }
    }
}
