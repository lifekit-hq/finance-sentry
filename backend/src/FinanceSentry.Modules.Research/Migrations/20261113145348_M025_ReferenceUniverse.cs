using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Research.Migrations
{
    /// <inheritdoc />
    public partial class M025_ReferenceUniverse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reference_list_members",
                schema: "research",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ListKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Ticker = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reference_list_members", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "reference_refresh_runs",
                schema: "research",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    MemberCount = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reference_refresh_runs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_reference_members_list_ticker",
                schema: "research",
                table: "reference_list_members",
                columns: new[] { "ListKey", "Ticker" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_reference_members_source",
                schema: "research",
                table: "reference_list_members",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "idx_reference_members_ticker",
                schema: "research",
                table: "reference_list_members",
                column: "Ticker");

            migrationBuilder.CreateIndex(
                name: "idx_reference_runs_source_finished",
                schema: "research",
                table: "reference_refresh_runs",
                columns: new[] { "Source", "FinishedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reference_list_members",
                schema: "research");

            migrationBuilder.DropTable(
                name: "reference_refresh_runs",
                schema: "research");
        }
    }
}
