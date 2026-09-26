using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Alerts.Migrations
{
    /// <inheritdoc />
    public partial class M004_AddOccurrenceCounter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OccurrenceCount",
                schema: "alerts",
                table: "alerts",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastOccurredAt",
                schema: "alerts",
                table: "alerts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OccurrenceCount",
                schema: "alerts",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "LastOccurredAt",
                schema: "alerts",
                table: "alerts");
        }
    }
}
