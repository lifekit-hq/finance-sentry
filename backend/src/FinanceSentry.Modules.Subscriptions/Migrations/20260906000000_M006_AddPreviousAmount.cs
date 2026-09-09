using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Subscriptions.Migrations
{
    /// <inheritdoc />
    public partial class M006_AddPreviousAmount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PreviousAmount",
                table: "detected_subscriptions",
                type: "numeric(15,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreviousAmount",
                table: "detected_subscriptions");
        }
    }
}
