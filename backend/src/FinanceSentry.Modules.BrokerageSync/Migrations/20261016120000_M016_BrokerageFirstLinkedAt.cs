using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.BrokerageSync.Migrations
{
    /// <inheritdoc />
    public partial class M016_BrokerageFirstLinkedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FirstLinkedAt",
                schema: "brokerage_sync",
                table: "IBKRCredentials",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstLinkedAt",
                schema: "brokerage_sync",
                table: "IBKRFlexCredentials",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Backfill: the best evidence a row still holds is its current CreatedAt (already re-stamped for a past
            // relink, so a row relinked before this migration keeps the relink day - the gap cannot be recovered).
            migrationBuilder.Sql("UPDATE brokerage_sync.\"IBKRCredentials\" SET \"FirstLinkedAt\" = \"CreatedAt\";");
            migrationBuilder.Sql("UPDATE brokerage_sync.\"IBKRFlexCredentials\" SET \"FirstLinkedAt\" = \"CreatedAt\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirstLinkedAt",
                schema: "brokerage_sync",
                table: "IBKRCredentials");

            migrationBuilder.DropColumn(
                name: "FirstLinkedAt",
                schema: "brokerage_sync",
                table: "IBKRFlexCredentials");
        }
    }
}
