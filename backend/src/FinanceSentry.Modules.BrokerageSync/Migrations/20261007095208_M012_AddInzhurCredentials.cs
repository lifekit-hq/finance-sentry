using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.BrokerageSync.Migrations
{
    /// <inheritdoc />
    public partial class M012_AddInzhurCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InzhurCredentials",
                schema: "brokerage_sync",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSyncAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSyncError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SessionStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SessionRefreshedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LoginAttemptsDay = table.Column<DateOnly>(type: "date", nullable: true),
                    LoginAttempts = table.Column<int>(type: "integer", nullable: false),
                    EncryptedPhone = table.Column<byte[]>(type: "bytea", nullable: false),
                    PhoneIv = table.Column<byte[]>(type: "bytea", nullable: false),
                    PhoneAuthTag = table.Column<byte[]>(type: "bytea", nullable: false),
                    EncryptedPassword = table.Column<byte[]>(type: "bytea", nullable: false),
                    PasswordIv = table.Column<byte[]>(type: "bytea", nullable: false),
                    PasswordAuthTag = table.Column<byte[]>(type: "bytea", nullable: false),
                    EncryptedSession = table.Column<byte[]>(type: "bytea", nullable: false),
                    SessionIv = table.Column<byte[]>(type: "bytea", nullable: false),
                    SessionAuthTag = table.Column<byte[]>(type: "bytea", nullable: false),
                    KeyVersion = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    SessionKeyVersion = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    Health_State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Health_ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    Health_FirstFailureAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Health_LastFailureAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Health_LastSuccessAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Health_LastFailureClass = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Health_LastFailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Health_SuspectSince = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Health_StateChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InzhurCredentials", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InzhurCredentials_UserId",
                schema: "brokerage_sync",
                table: "InzhurCredentials",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InzhurCredentials",
                schema: "brokerage_sync");
        }
    }
}
