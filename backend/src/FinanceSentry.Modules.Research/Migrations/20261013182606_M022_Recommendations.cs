using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Research.Migrations
{
    /// <inheritdoc />
    public partial class M022_Recommendations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recommendations",
                schema: "research",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ticker = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TriggerKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TriggerRef = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Rationale = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Confidence = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    InvalidationTrigger = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SizeUsd = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    SizeShares = table.Column<decimal>(type: "numeric(22,8)", precision: 22, scale: 8, nullable: true),
                    ShareOfBookBeforePct = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    ShareOfBookAfterPct = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    LimitPrice = table.Column<decimal>(type: "numeric(22,12)", precision: 22, scale: 12, nullable: true),
                    LimitCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Venue = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    OrderText = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SupersededById = table.Column<Guid>(type: "uuid", nullable: true),
                    Execution = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubjectReturnPct = table.Column<decimal>(type: "numeric(12,4)", nullable: true),
                    BenchmarkReturnPct = table.Column<decimal>(type: "numeric(12,4)", nullable: true),
                    OutcomeScoredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Evidence = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recommendations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_recommendations_user_status_created",
                schema: "research",
                table: "recommendations",
                columns: new[] { "UserId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "idx_recommendations_user_ticker_trigger",
                schema: "research",
                table: "recommendations",
                columns: new[] { "UserId", "Ticker", "TriggerKind" });

            migrationBuilder.CreateIndex(
                name: "ux_recommendations_user_ticker_pending",
                schema: "research",
                table: "recommendations",
                columns: new[] { "UserId", "Ticker" },
                unique: true,
                filter: "\"Status\" = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recommendations",
                schema: "research");
        }
    }
}
