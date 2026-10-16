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

            // Backfill with the earliest evidence a relink cannot have moved, per user: both IBKR credentials' CreatedAt
            // (CreatedAt itself carries any past relink) and the first time a synced IBKR cash row or trade was stored
            // (their CreatedAt is the insert time). BrokerageHolding.SyncedAt is rewritten on every sync, so it is no evidence.
            foreach (var table in new[] { "IBKRCredentials", "IBKRFlexCredentials" })
            {
                migrationBuilder.Sql($"""
                    UPDATE brokerage_sync."{table}" AS c
                    SET "FirstLinkedAt" = LEAST(
                        c."CreatedAt",
                        COALESCE((SELECT MIN(o."CreatedAt") FROM brokerage_sync."IBKRCredentials" o WHERE o."UserId" = c."UserId"), c."CreatedAt"),
                        COALESCE((SELECT MIN(o."CreatedAt") FROM brokerage_sync."IBKRFlexCredentials" o WHERE o."UserId" = c."UserId"), c."CreatedAt"),
                        COALESCE((SELECT MIN(t."CreatedAt") FROM brokerage_sync."BrokerageCashTransactions" t WHERE t."UserId" = c."UserId" AND lower(t."Provider") = 'ibkr'), c."CreatedAt"),
                        COALESCE((SELECT MIN(t."CreatedAt") FROM brokerage_sync."BrokerageTrades" t WHERE t."UserId" = c."UserId" AND lower(t."Provider") = 'ibkr'), c."CreatedAt"));
                    """);
            }
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
