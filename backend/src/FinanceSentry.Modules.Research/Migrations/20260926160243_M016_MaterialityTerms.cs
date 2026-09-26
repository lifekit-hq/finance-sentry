using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Research.Migrations
{
    /// <inheritdoc />
    public partial class M016_MaterialityTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "materiality_terms",
                schema: "research",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Term = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_materiality_terms", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_materiality_terms_term",
                schema: "research",
                table: "materiality_terms",
                column: "Term",
                unique: true);

            // Seed with the original hardcoded keyword array (#693) so behavior is unchanged until an
            // operator retunes the list.
            migrationBuilder.InsertData(
                schema: "research",
                table: "materiality_terms",
                columns: ["Id", "Term", "Enabled"],
                values: new object[,]
                {
                    { Guid.NewGuid(), "guidance", true },
                    { Guid.NewGuid(), "downgrade", true },
                    { Guid.NewGuid(), "investigation", true },
                    { Guid.NewGuid(), "M&A", true },
                    { Guid.NewGuid(), "halted", true },
                    { Guid.NewGuid(), "recall", true },
                    { Guid.NewGuid(), "acquisition", true },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "materiality_terms",
                schema: "research");
        }
    }
}
