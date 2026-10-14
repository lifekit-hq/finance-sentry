using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M018_AccountClosures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountClosures",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PurgeDueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CredentialRevocation = table.Column<string>(type: "jsonb", nullable: true),
                    ReopenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReopenedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PurgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErasedRows = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountClosures", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountClosures_PurgeDueAt",
                schema: "auth",
                table: "AccountClosures",
                column: "PurgeDueAt");

            migrationBuilder.CreateIndex(
                name: "IX_AccountClosures_UserId_Pending",
                schema: "auth",
                table: "AccountClosures",
                column: "UserId",
                unique: true,
                filter: "\"ReopenedAt\" IS NULL AND \"PurgedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountClosures",
                schema: "auth");
        }
    }
}
