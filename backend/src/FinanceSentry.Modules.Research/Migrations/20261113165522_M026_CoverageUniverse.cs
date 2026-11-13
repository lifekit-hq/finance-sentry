using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Research.Migrations
{
    /// <inheritdoc />
    public partial class M026_CoverageUniverse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "coverage_journal",
                schema: "research",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ticker = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Action = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Rule = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CorrelationToBook = table.Column<decimal>(type: "numeric(6,4)", precision: 6, scale: 4, nullable: true),
                    RankScore = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coverage_journal", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "coverage_members",
                schema: "research",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ticker = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    AddedReason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LastEventAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastScoreId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationToBook = table.Column<decimal>(type: "numeric(6,4)", precision: 6, scale: 4, nullable: true),
                    RemoveAfter = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coverage_members", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "coverage_verdict_observations",
                schema: "research",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ticker = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ObservedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Verdict = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FiredRuleId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: true),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coverage_verdict_observations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_coverage_journal_user_ticker_at",
                schema: "research",
                table: "coverage_journal",
                columns: new[] { "UserId", "Ticker", "At" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ux_coverage_members_user_ticker",
                schema: "research",
                table: "coverage_members",
                columns: new[] { "UserId", "Ticker" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_coverage_observations_day",
                schema: "research",
                table: "coverage_verdict_observations",
                column: "ObservedOn");

            migrationBuilder.CreateIndex(
                name: "ux_coverage_observations_user_ticker_day",
                schema: "research",
                table: "coverage_verdict_observations",
                columns: new[] { "UserId", "Ticker", "ObservedOn" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "coverage_journal",
                schema: "research");

            migrationBuilder.DropTable(
                name: "coverage_members",
                schema: "research");

            migrationBuilder.DropTable(
                name: "coverage_verdict_observations",
                schema: "research");
        }
    }
}
