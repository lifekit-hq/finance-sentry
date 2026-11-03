using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Subscriptions.Migrations
{
    /// <inheritdoc />
    public partial class M004_AddEndDateAndPlanRekey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "EndDate",
                table: "detected_subscriptions",
                type: "date",
                nullable: true);

            // One-time data repair for rows created under the old detection rules. Every
            // statement is a no-op on a database that doesn't hold the matching rows.

            // Legacy rows keyed by the raw installment description (pre-prefix-stripping)
            // duplicate the properly-keyed merchant rows.
            migrationBuilder.Sql(
                """
                DELETE FROM public.detected_subscriptions
                WHERE "IsManual" = false AND "Kind" = 'installment'
                  AND ("MerchantNameDisplay" LIKE 'Погашення наступного платежу%'
                       OR "MerchantNameDisplay" LIKE 'Щомісячний платіж%');
                """);

            // Per-plan identity: installment keys now embed the rounded monthly amount
            // ("installment:тов алло" → "installment:тов алло:2340") so two concurrent
            // plans at the same shop stay separate rows. round() is half-away-from-zero,
            // matching InstallmentPlanRecognizer.RoundPlanAmount.
            migrationBuilder.Sql(
                """
                UPDATE public.detected_subscriptions
                SET "MerchantNameNormalized" = "MerchantNameNormalized" || ':' || round("LastKnownAmount")::text
                WHERE "IsManual" = false AND "Kind" = 'installment'
                  AND "MerchantNameNormalized" LIKE 'installment:%'
                  AND "MerchantNameNormalized" !~ ':[0-9]+$';
                """);

            // The recurring ₴14k transfer to a masked card is a mortgage (final payment
            // May 2036), not a service subscription: reclassify so it leaves the recurring
            // spend summary and reports remaining payments from the end date. Detection
            // keeps updating its charges but can no longer overwrite the display name
            // with a PAN.
            migrationBuilder.Sql(
                """
                UPDATE public.detected_subscriptions
                SET "Kind" = 'installment', "MerchantNameDisplay" = 'Іпотека', "EndDate" = DATE '2036-05-01'
                WHERE "IsManual" = false AND "Kind" = 'subscription' AND "MerchantNameNormalized" = '400000';
                """);

            // Hand-inserted manual rows whose merchants the detector can now find on its
            // own: re-key them to the detector's identity and hand them over (IsManual off)
            // so occurrences and dates stay current automatically. TermCount is preserved.
            migrationBuilder.Sql(
                """
                UPDATE public.detected_subscriptions AS m
                SET "IsManual" = false, "MerchantNameNormalized" = 'claude'
                WHERE m."MerchantNameNormalized" = 'manual:subscription:claude (anthropic)'
                  AND NOT EXISTS (
                      SELECT 1 FROM public.detected_subscriptions d
                      WHERE d."UserId" = m."UserId" AND d."MerchantNameNormalized" = 'claude');
                """);

            migrationBuilder.Sql(
                """
                UPDATE public.detected_subscriptions AS m
                SET "IsManual" = false, "MerchantNameNormalized" = 'mobile top-up 0057',
                    "MerchantNameDisplay" = 'Mobile top-up 0057'
                WHERE m."MerchantNameNormalized" = 'manual:subscription:mobile top-up'
                  AND NOT EXISTS (
                      SELECT 1 FROM public.detected_subscriptions d
                      WHERE d."UserId" = m."UserId" AND d."MerchantNameNormalized" = 'mobile top-up 0057');
                """);

            migrationBuilder.Sql(
                """
                UPDATE public.detected_subscriptions AS m
                SET "IsManual" = false, "MerchantNameNormalized" = 'installment:тов алло:3000',
                    "MerchantNameDisplay" = 'ТОВ Алло'
                WHERE m."MerchantNameNormalized" = 'manual:installment:тов алло (2)'
                  AND NOT EXISTS (
                      SELECT 1 FROM public.detected_subscriptions d
                      WHERE d."UserId" = m."UserId" AND d."MerchantNameNormalized" = 'installment:тов алло:3000');
                """);

            // If a target key already existed (detection re-created it between data entry
            // and this deploy), the manual duplicate is simply dropped — the detected row
            // owns the identity from here on.
            migrationBuilder.Sql(
                """
                DELETE FROM public.detected_subscriptions
                WHERE "MerchantNameNormalized" IN (
                    'manual:subscription:claude (anthropic)',
                    'manual:subscription:mobile top-up',
                    'manual:installment:тов алло (2)');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndDate",
                table: "detected_subscriptions");
        }
    }
}
