using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Research.Migrations
{
    /// <inheritdoc />
    public partial class M027_CoverageEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "coverage_events",
                schema: "research",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ticker = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    Disposition = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Rescored = table.Column<bool>(type: "boolean", nullable: false),
                    ReviewNotice = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Verdict = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Rule = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coverage_events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_coverage_events_user_occurred",
                schema: "research",
                table: "coverage_events",
                columns: new[] { "UserId", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_coverage_events_identity",
                schema: "research",
                table: "coverage_events",
                columns: new[] { "UserId", "Ticker", "Source", "Reference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "coverage_events",
                schema: "research");
        }
    }
}
