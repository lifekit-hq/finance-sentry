using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.BrokerageSync.Migrations
{
    /// <inheritdoc />
    public partial class M008_AddIbkrFlexCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IBKRFlexCredentials",
                schema: "brokerage_sync",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    QueryId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EncryptedToken = table.Column<byte[]>(type: "bytea", nullable: false),
                    TokenIv = table.Column<byte[]>(type: "bytea", nullable: false),
                    TokenAuthTag = table.Column<byte[]>(type: "bytea", nullable: false),
                    KeyVersion = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IBKRFlexCredentials", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IBKRFlexCredentials_UserId",
                schema: "brokerage_sync",
                table: "IBKRFlexCredentials",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IBKRFlexCredentials",
                schema: "brokerage_sync");
        }
    }
}
