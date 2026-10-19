using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.BankSync.Migrations
{
    /// <summary>
    /// One-off data convergence (#1151): the seeder is insert-only, so the live rows for MCC 4214, 4215,
    /// 4225 (GENERAL_SERVICES) and 5832 (GENERAL_MERCHANDISE) kept the UNCATEGORIZED they were seeded with.
    /// Each row is rewritten only while it still says UNCATEGORIZED (a fresh database was seeded with the
    /// right value; a hand edit stays), and only transactions still UNCATEGORIZED follow it.
    /// </summary>
    [DbContext(typeof(BankSyncDbContext))]
    [Migration("20261019120000_M028_ConvergeMccCategories")]
    public partial class M028_ConvergeMccCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Transactions first: they join the rows that are still stale.
            migrationBuilder.Sql(
                """
                UPDATE bank_sync."Transactions" t
                SET "MerchantCategory" = CASE WHEN m."Mcc" = 5832 THEN 'GENERAL_MERCHANDISE' ELSE 'GENERAL_SERVICES' END,
                    "UpdatedAt" = CURRENT_TIMESTAMP
                FROM bank_sync.mcc_categories m
                WHERE m."Mcc" = t."Mcc"
                  AND m."Mcc" IN (4214, 4215, 4225, 5832)
                  AND m."CategoryKey" = 'UNCATEGORIZED'
                  AND t."MerchantCategory" = 'UNCATEGORIZED';
                """);

            migrationBuilder.Sql(
                """
                UPDATE bank_sync.mcc_categories
                SET "CategoryKey" = CASE WHEN "Mcc" = 5832 THEN 'GENERAL_MERCHANDISE' ELSE 'GENERAL_SERVICES' END
                WHERE "Mcc" IN (4214, 4215, 4225, 5832)
                  AND "CategoryKey" = 'UNCATEGORIZED';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: a fresh database was seeded with these values, so restoring UNCATEGORIZED would corrupt it.
        }
    }
}
