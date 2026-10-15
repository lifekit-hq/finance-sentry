using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.BrokerageSync.Migrations
{
    /// <inheritdoc />
    public partial class M015_ConnectionHealthNotBefore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_NotBefore",
                schema: "brokerage_sync",
                table: "IBKRCredentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_NotBefore",
                schema: "brokerage_sync",
                table: "IBKRFlexCredentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Health_NotBefore",
                schema: "brokerage_sync",
                table: "InzhurCredentials",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Health_NotBefore",
                schema: "brokerage_sync",
                table: "IBKRCredentials");

            migrationBuilder.DropColumn(
                name: "Health_NotBefore",
                schema: "brokerage_sync",
                table: "IBKRFlexCredentials");

            migrationBuilder.DropColumn(
                name: "Health_NotBefore",
                schema: "brokerage_sync",
                table: "InzhurCredentials");
        }
    }
}
