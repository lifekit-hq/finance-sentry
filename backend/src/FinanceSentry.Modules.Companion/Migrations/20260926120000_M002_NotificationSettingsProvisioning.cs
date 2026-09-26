using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Companion.Migrations
{
    /// <summary>
    /// Data-only migration (issue #686): backfills a default notification settings row for every
    /// existing user who does not already have one, and expires pre-existing held-for-digest events
    /// that predate this backfill so they are never silently delivered stale on the next digest tick.
    /// No schema/entity change — the model snapshot is unaffected.
    /// </summary>
    public partial class M002_NotificationSettingsProvisioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO companion.companion_notification_settings
                    ("Id", "UserId", "Mode", "QuietHoursStartLocal", "QuietHoursEndLocal",
                     "TimeZoneId", "MaxProactivePerHour", "DigestHourLocal", "UpdatedAt")
                SELECT
                    gen_random_uuid(),
                    u."Id"::uuid,
                    'Scan',
                    22,
                    7,
                    'Europe/Dublin',
                    6,
                    8,
                    now()
                FROM auth."AspNetUsers" u
                WHERE NOT EXISTS (
                    SELECT 1 FROM companion.companion_notification_settings s
                    WHERE s."UserId" = u."Id"::uuid
                );
                """);

            migrationBuilder.Sql(
                """
                UPDATE companion.companion_events
                SET "Disposition" = 'Expired',
                    "LastError" = 'Expired by migration 20260926120000_M002 (issue #686): held for digest before notification settings backfill, no scheduled delivery path existed.'
                WHERE "Disposition" = 'HeldForDigest';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data-only migration; the backfilled settings rows and expired dispositions are not reverted.
        }
    }
}
