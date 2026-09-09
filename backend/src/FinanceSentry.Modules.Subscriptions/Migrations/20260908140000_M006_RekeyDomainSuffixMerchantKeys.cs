using FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Subscriptions.Migrations
{
    /// <summary>
    /// One-time rekey for spec 554/US3: <c>MerchantNameNormalizer</c> now strips a domain suffix
    /// that was hidden behind trailing statement digits ("NETFLIX.COM 866-5797172" keyed
    /// <c>netflix.com</c>, now <c>netflix</c>), so a row minted under the old derivation stops
    /// matching the key detection derives today — its commitment drops out of the committed
    /// split and the next detection run adds a second row beside it. Same repair as M004's plan
    /// rekey, and a no-op on a database holding no such rows.
    /// <para>
    /// <b>Deliberately narrow.</b> This does not re-implement the normalizer in SQL. It rekeys
    /// only rows where stripping the suffix demonstrably lands on the key the normalizer derives
    /// today: no <c>paypal*</c> prefix, no leading punctuation, no digits or whitespace revealed
    /// underneath, no brand alias to fire. Anything else keeps its key and heals the way it
    /// already would — detection re-derives it on the next run.
    /// </para>
    /// </summary>
    [DbContext(typeof(SubscriptionsDbContext))]
    [Migration("20260908140000_M006_RekeyDomainSuffixMerchantKeys")]
    public partial class M006_RekeyDomainSuffixMerchantKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Manual rows are excluded throughout: their key is manual:{kind}:{display}, not
            // normalizer output, and the identity is the user's to name.
            // DISTINCT ON keeps one row per (user, target) and NOT EXISTS rules out a collision
            // with an untouched row, so the unique (UserId, MerchantNameNormalized) index holds.
            // A row that loses either test keeps its old key rather than being deleted: it may
            // carry state the user set (a dismissal, a term count, an end date) that detection
            // would not restore.
            migrationBuilder.Sql(
                """
                WITH candidates AS (
                    SELECT s."Id", s."UserId",
                           regexp_replace(s."MerchantNameNormalized",
                                          '(\.(com|net|io|co|org))+$', '') AS target
                    FROM public.detected_subscriptions s
                    WHERE s."IsManual" = false
                      AND s."MerchantNameNormalized" ~ '\.(com|net|io|co|org)$'
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
                          SELECT 1 FROM public.detected_subscriptions d
                          WHERE d."UserId" = c."UserId" AND d."MerchantNameNormalized" = c.target)
                    ORDER BY c."UserId", c.target, c."Id"
                )
                UPDATE public.detected_subscriptions AS s
                SET "MerchantNameNormalized" = w.target
                FROM winners w
                WHERE s."Id" = w."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible by nature: the stripped suffix is not recoverable from the new key,
            // and detection re-derives these rows anyway.
        }
    }
}
