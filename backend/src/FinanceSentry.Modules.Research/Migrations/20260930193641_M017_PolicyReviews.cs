using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Research.Migrations
{
    /// <inheritdoc />
    public partial class M017_PolicyReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "policy_reviews",
                schema: "research",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyStatementId = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyStatementVersion = table.Column<int>(type: "integer", nullable: false),
                    ReviewCadence = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DaysOverdue = table.Column<int>(type: "integer", nullable: false),
                    WasMissed = table.Column<bool>(type: "boolean", nullable: false),
                    TotalValueUsd = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Sleeves = table.Column<string>(type: "jsonb", nullable: false),
                    Adjustments = table.Column<string>(type: "jsonb", nullable: false),
                    Rationale = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_policy_reviews", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_policy_reviews_user_completed",
                schema: "research",
                table: "policy_reviews",
                columns: new[] { "UserId", "CompletedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "policy_reviews",
                schema: "research");
        }
    }
}
