using FinanceSentry.Modules.Alerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Alerts.Migrations
{
    /// <summary>
    /// Data-only repair (finance-sentry#690): the allocation-drift check matched an IPS target's
    /// AssetClass against the coarse RiskSleeve grouping instead of the AssetClass taxonomy, so it
    /// could never find a match for a non-crypto target and always fell back to reporting a 0%
    /// observed weight — a structurally impossible reading for a sleeve that actually holds
    /// positions. Resolves every still-open PolicyViolation alert carrying that shape; the fixed
    /// detector will raise a fresh, correctly-computed alert on the next run if the sleeve is still
    /// out of band.
    /// </summary>
    [DbContext(typeof(AlertsDbContext))]
    [Migration("20260926140000_M005_ResolveZeroObservedAllocationDriftAlerts")]
    public partial class M005_ResolveZeroObservedAllocationDriftAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE alerts."alerts"
                SET "IsResolved" = true, "ResolvedAt" = CURRENT_TIMESTAMP, "UpdatedAt" = CURRENT_TIMESTAMP
                WHERE "Type" = 'PolicyViolation'
                  AND "IsResolved" = false
                  AND "Message" LIKE 'AllocationDrift breached for%: observed 0,%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data repair — the pre-repair zero-observation alerts are not worth restoring.
        }
    }
}
