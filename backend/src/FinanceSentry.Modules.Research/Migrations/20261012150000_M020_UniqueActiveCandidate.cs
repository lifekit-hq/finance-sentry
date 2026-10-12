using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Research.Migrations
{
    /// <inheritdoc />
    public partial class M020_UniqueActiveCandidate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fail clearly rather than delete: a duplicate Active (UserId, Ticker) pair needs a person to pick the row to keep.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE duplicates integer;
                BEGIN
                    SELECT count(*) INTO duplicates FROM (
                        SELECT 1 FROM research.opportunity_candidates
                        WHERE "Status" = 'Active'
                        GROUP BY "UserId", "Ticker"
                        HAVING count(*) > 1) d;
                    IF duplicates > 0 THEN
                        RAISE EXCEPTION 'M020_UniqueActiveCandidate: % (UserId, Ticker) pairs have more than one Active opportunity candidate; resolve them before migrating', duplicates;
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "ux_opportunity_candidates_user_ticker_active",
                schema: "research",
                table: "opportunity_candidates",
                columns: new[] { "UserId", "Ticker" },
                unique: true,
                filter: "\"Status\" = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_opportunity_candidates_user_ticker_active",
                schema: "research",
                table: "opportunity_candidates");
        }
    }
}
