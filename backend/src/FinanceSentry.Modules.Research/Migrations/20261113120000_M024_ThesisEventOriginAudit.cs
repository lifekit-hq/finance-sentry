using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Research.Migrations
{
    /// <inheritdoc />
    public partial class M024_ThesisEventOriginAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Additive only: rows written before this migration read as User (the default) with no audit payload.
            migrationBuilder.AddColumn<string>(
                name: "Origin",
                schema: "research",
                table: "thesis_events",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "User");

            migrationBuilder.AddColumn<string>(
                name: "Audit",
                schema: "research",
                table: "thesis_events",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Audit",
                schema: "research",
                table: "thesis_events");

            migrationBuilder.DropColumn(
                name: "Origin",
                schema: "research",
                table: "thesis_events");
        }
    }
}
