using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Research.Migrations
{
    /// <inheritdoc />
    public partial class M018_BenchmarkRelativeRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "benchmark_relative_records",
                schema: "research",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AsOf = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ScopeKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ThesisId = table.Column<Guid>(type: "uuid", nullable: true),
                    Window = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BenchmarkTicker = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Covered = table.Column<bool>(type: "boolean", nullable: false),
                    ConstituentCount = table.Column<int>(type: "integer", nullable: false),
                    FromTimestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ToTimestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubjectReturnPct = table.Column<decimal>(type: "numeric(12,4)", nullable: true),
                    BenchmarkReturnPct = table.Column<decimal>(type: "numeric(12,4)", nullable: true),
                    ExcessReturnPct = table.Column<decimal>(type: "numeric(12,4)", nullable: true),
                    NetExcessReturnPct = table.Column<decimal>(type: "numeric(12,4)", nullable: true),
                    NetGate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UnderperformingRuns = table.Column<int>(type: "integer", nullable: false),
                    SustainedUnderperformance = table.Column<bool>(type: "boolean", nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_benchmark_relative_records", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_benchmark_relative_records_run",
                schema: "research",
                table: "benchmark_relative_records",
                columns: new[] { "UserId", "AsOf", "Scope", "ScopeKey", "Window" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "benchmark_relative_records",
                schema: "research");
        }
    }
}
