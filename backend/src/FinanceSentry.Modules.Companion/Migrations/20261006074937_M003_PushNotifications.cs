using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Companion.Migrations
{
    /// <inheritdoc />
    public partial class M003_PushNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PushEnabled",
                schema: "companion",
                table: "companion_notification_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "push_subscriptions",
                schema: "companion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    P256dh = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Auth = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DeviceLabel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSuccessAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastFailureAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureCount = table.Column<int>(type: "integer", nullable: false),
                    DisabledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_push_subscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "push_deliveries",
                schema: "companion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastStatusCode = table.Column<int>(type: "integer", nullable: true),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_push_deliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_push_deliveries_companion_events_EventId",
                        column: x => x.EventId,
                        principalSchema: "companion",
                        principalTable: "companion_events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_push_deliveries_push_subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalSchema: "companion",
                        principalTable: "push_subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_push_deliveries_EventId_SubscriptionId",
                schema: "companion",
                table: "push_deliveries",
                columns: new[] { "EventId", "SubscriptionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_push_deliveries_Status_NextAttemptAt",
                schema: "companion",
                table: "push_deliveries",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_push_deliveries_SubscriptionId",
                schema: "companion",
                table: "push_deliveries",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_push_subscriptions_Endpoint",
                schema: "companion",
                table: "push_subscriptions",
                column: "Endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_push_subscriptions_UserId",
                schema: "companion",
                table: "push_subscriptions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "push_deliveries",
                schema: "companion");

            migrationBuilder.DropTable(
                name: "push_subscriptions",
                schema: "companion");

            migrationBuilder.DropColumn(
                name: "PushEnabled",
                schema: "companion",
                table: "companion_notification_settings");
        }
    }
}
