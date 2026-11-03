using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.BankSync.Migrations
{
    /// <summary>
    /// Privacy and correctness fix (release-readiness S2): M011, M014, M016 and M019 seeded counterparties that
    /// describe one person's household as system defaults (<c>UserId = Guid.Empty</c>), and
    /// <c>CounterpartyRepository.GetForUserAsync</c> applies those to every user. This re-keys the owner-specific ones
    /// to the owner's own <c>UserId</c>; the generic default (investment routing, M012) keeps <c>Guid.Empty</c>.
    ///
    /// <para>
    /// The owner is the single account holding the <c>Owner</c> role in the Auth module's tables - the same source
    /// <c>RoleSeeder</c> writes, and Auth always migrates before BankSync. With no Owner, or more than one, the
    /// migration changes nothing rather than guess whose household this is. Only rows still on <c>Guid.Empty</c> are
    /// touched, so a re-run is a no-op, and the rows are copied to <c>bank_sync.counterparties_rekey_backup</c> first.
    /// </para>
    /// </summary>
    [DbContext(typeof(BankSyncDbContext))]
    [Migration("20261103120000_M030_RekeyOwnerCounterparties")]
    public partial class M030_RekeyOwnerCounterparties : Migration
    {
        // Ids seeded by M011 (…0001, …0002), M014 (…0004), M016 (…0005) and M019 (…0006). …0003 is M012's
        // investment-routing default: venue names, not a person's data, so it stays a system default.
        private const string OwnerSpecificIds =
            "'11111111-0000-0000-0000-000000000001', '11111111-0000-0000-0000-000000000002', " +
            "'11111111-0000-0000-0000-000000000004', '11111111-0000-0000-0000-000000000005', " +
            "'11111111-0000-0000-0000-000000000006'";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $$"""
                DO $rekey$
                DECLARE
                    owner_ids uuid[];
                BEGIN
                    IF to_regclass('auth."AspNetUserRoles"') IS NULL OR to_regclass('auth."AspNetRoles"') IS NULL THEN
                        RAISE NOTICE 'M030: Auth tables not found; owner counterparties left as system defaults.';
                        RETURN;
                    END IF;

                    SELECT COALESCE(array_agg(DISTINCT ur."UserId"::uuid), ARRAY[]::uuid[]) INTO owner_ids
                    FROM auth."AspNetUserRoles" ur
                    JOIN auth."AspNetRoles" r ON r."Id" = ur."RoleId"
                    WHERE r."NormalizedName" = 'OWNER'
                      AND ur."UserId" ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$';

                    IF cardinality(owner_ids) <> 1 THEN
                        RAISE NOTICE 'M030: % owner account(s) found, need exactly 1; owner counterparties left as system defaults.',
                            cardinality(owner_ids);
                        RETURN;
                    END IF;

                    CREATE TABLE IF NOT EXISTS bank_sync.counterparties_rekey_backup
                        (LIKE bank_sync.counterparties);

                    INSERT INTO bank_sync.counterparties_rekey_backup
                    SELECT c.*
                    FROM bank_sync.counterparties c
                    WHERE c."Id" IN ({{OwnerSpecificIds}})
                      AND c."UserId" = '00000000-0000-0000-0000-000000000000'
                      AND NOT EXISTS (SELECT 1 FROM bank_sync.counterparties_rekey_backup b WHERE b."Id" = c."Id");

                    UPDATE bank_sync.counterparties
                    SET "UserId" = owner_ids[1], "UpdatedAt" = CURRENT_TIMESTAMP
                    WHERE "Id" IN ({{OwnerSpecificIds}})
                      AND "UserId" = '00000000-0000-0000-0000-000000000000';
                END
                $rekey$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restores the original keys from the backup (kept in place, never dropped). Without a backup there is
            // nothing this migration changed.
            migrationBuilder.Sql(
                """
                DO $restore$
                BEGIN
                    IF to_regclass('bank_sync.counterparties_rekey_backup') IS NULL THEN
                        RETURN;
                    END IF;

                    UPDATE bank_sync.counterparties c
                    SET "UserId" = b."UserId", "UpdatedAt" = CURRENT_TIMESTAMP
                    FROM bank_sync.counterparties_rekey_backup b
                    WHERE c."Id" = b."Id"
                      AND c."UserId" <> b."UserId";
                END
                $restore$;
                """);
        }
    }
}
