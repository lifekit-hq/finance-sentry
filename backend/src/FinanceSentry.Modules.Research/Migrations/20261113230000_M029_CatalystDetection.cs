using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Research.Migrations
{
    /// <inheritdoc />
    public partial class M029_CatalystDetection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "catalyst_events",
                schema: "research",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ticker = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Accession = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Form = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Items = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    FilingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Summary = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DocumentUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Source = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    DetectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    Disposition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Tag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalyst_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "theme_attention_days",
                schema: "research",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Theme = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Phrase = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Articles = table.Column<long>(type: "bigint", nullable: false),
                    TotalArticles = table.Column<long>(type: "bigint", nullable: true),
                    Source = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    MeasuredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_theme_attention_days", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_catalyst_events_user_detected",
                schema: "research",
                table: "catalyst_events",
                columns: new[] { "UserId", "DetectedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_catalyst_events_identity",
                schema: "research",
                table: "catalyst_events",
                columns: new[] { "UserId", "Ticker", "Accession" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_theme_attention_days_day",
                schema: "research",
                table: "theme_attention_days",
                column: "Day");

            migrationBuilder.CreateIndex(
                name: "ux_theme_attention_days_identity",
                schema: "research",
                table: "theme_attention_days",
                columns: new[] { "Theme", "Phrase", "Day" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalyst_events",
                schema: "research");

            migrationBuilder.DropTable(
                name: "theme_attention_days",
                schema: "research");
        }
    }
}
