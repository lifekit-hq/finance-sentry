using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Research.Migrations
{
    /// <inheritdoc />
    public partial class M028_ThesisNarrativeFalsifiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Additive only: a NOT NULL column with a constant default, so existing theses read back with no
            // narrative falsifiers and no row is rewritten by application code.
            migrationBuilder.AddColumn<string>(
                name: "NarrativeFalsifiers",
                schema: "research",
                table: "theses",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rollback drops the column and with it every narrative falsifier saved since; the thesis_events audit
            // payloads keep their own copy of the lists they recorded.
            migrationBuilder.DropColumn(
                name: "NarrativeFalsifiers",
                schema: "research",
                table: "theses");
        }
    }
}
