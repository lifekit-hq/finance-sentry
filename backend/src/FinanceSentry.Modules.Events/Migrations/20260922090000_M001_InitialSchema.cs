using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Events.Migrations
{
    /// <inheritdoc />
    public partial class M001_InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "events");

            migrationBuilder.CreateTable(
                name: "event_verdicts",
                schema: "events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanionEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Verdict = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Notified = table.Column<bool>(type: "boolean", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_verdicts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_event_verdicts_UserId_CompanionEventId",
                schema: "events",
                table: "event_verdicts",
                columns: new[] { "UserId", "CompanionEventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_verdicts",
                schema: "events");
        }
    }
}
