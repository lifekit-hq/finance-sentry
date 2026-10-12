using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FinanceSentry.Modules.Companion.Migrations
{
    /// <inheritdoc />
    public partial class M006_ProblemReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "problem_reports",
                schema: "companion",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RoutePattern = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AppVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Device = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Client = table.Column<string>(type: "character varying(81)", maxLength: 81, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ForwardedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_problem_reports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_problem_reports_Status_NextAttemptAt",
                schema: "companion",
                table: "problem_reports",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_problem_reports_UserId_CreatedAt",
                schema: "companion",
                table: "problem_reports",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "problem_reports",
                schema: "companion");
        }
    }
}
