using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Wealth.Migrations
{
    /// <inheritdoc />
    public partial class M008_AddNetWorthSnapshotSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "net_worth_snapshot_sources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    cash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    invested = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    health = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reasons = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_net_worth_snapshot_sources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_net_worth_snapshot_sources_net_worth_snapshots_snapshot_id",
                        column: x => x.snapshot_id,
                        principalTable: "net_worth_snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_net_worth_snapshot_sources_snapshot_source",
                table: "net_worth_snapshot_sources",
                columns: new[] { "snapshot_id", "source_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "net_worth_snapshot_sources");
        }
    }
}
