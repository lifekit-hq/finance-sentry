using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.BankSync.Migrations
{
    /// <summary>
    /// The pin-side half of M006's rekey (spec 554/US3), under the same narrow rule: only keys
    /// whose stripped form is demonstrably what the normalizer derives today are touched. A pin
    /// left on the old key would silently stop claiming the merchant it names, and — unlike a
    /// detected subscription — nothing re-derives it, because a pin is a statement the user made
    /// once.
    /// </summary>
    [DbContext(typeof(BankSyncDbContext))]
    [Migration("20260908140100_M018_RekeyDomainSuffixPinKeys")]
    public partial class M018_RekeyDomainSuffixPinKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                WITH candidates AS (
                    SELECT p."Id", p."UserId",
                           regexp_replace(p."MerchantKey", '(\.(com|net|io|co|org))+$', '') AS target
                    FROM bank_sync.committed_merchant_pins p
                    WHERE p."MerchantKey" ~ '\.(com|net|io|co|org)$'
                ),
                winners AS (
                    SELECT DISTINCT ON (c."UserId", c.target) c."Id", c.target
                    FROM candidates c
                    WHERE c.target <> ''
                      AND c.target = btrim(c.target)
                      AND c.target !~ '\.(com|net|io|co|org)$'
                      AND c.target !~ '[\s\-_*#]+\d[\d\s\-_]*$'
                      AND c.target !~ '^[*# ]'
                      AND c.target !~ '\s\s'
                      AND c.target NOT LIKE 'paypal*%'
                      AND c.target !~ '(anthropic|claude|openai|chatgpt)'
                      AND NOT EXISTS (
                          SELECT 1 FROM bank_sync.committed_merchant_pins e
                          WHERE e."UserId" = c."UserId" AND e."MerchantKey" = c.target)
                    ORDER BY c."UserId", c.target, c."Id"
                )
                UPDATE bank_sync.committed_merchant_pins AS p
                SET "MerchantKey" = w.target
                FROM winners w
                WHERE p."Id" = w."Id";
                """);

            // A pin that lost the collision test names a merchant the user already holds pinned
            // under the stripped key. Unlike a subscription row it carries no other state, and
            // leaving it would strand a listing entry the user cannot remove: unpinning derives
            // the stripped key, which now belongs to the surviving pin.
            migrationBuilder.Sql(
                """
                DELETE FROM bank_sync.committed_merchant_pins AS p
                WHERE p."MerchantKey" ~ '\.(com|net|io|co|org)$'
                  AND EXISTS (
                      SELECT 1 FROM bank_sync.committed_merchant_pins e
                      WHERE e."UserId" = p."UserId" AND e."Id" <> p."Id"
                        AND e."MerchantKey" = regexp_replace(
                            p."MerchantKey", '(\.(com|net|io|co|org))+$', ''));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible: the stripped suffix cannot be recovered from the new key.
        }
    }
}
