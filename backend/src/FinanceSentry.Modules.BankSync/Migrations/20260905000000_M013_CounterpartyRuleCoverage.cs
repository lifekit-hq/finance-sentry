using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.BankSync.Migrations
{
    /// <summary>
    /// Data-only migration: widens the seeded counterparty rules to the wordings that
    /// actually appear on statements. The M011/M012 patterns only matched the Monobank
    /// (Cyrillic) renderings, so the same people and venues went unmatched on the
    /// TrueLayer side: Revolut writes "Oksana Lysenko" / "To Mariia M" in Latin
    /// script, and a Revolut X funding leg ("EUR → Revolut X") matched no investment
    /// rule at all. Unmatched legs fall through to transfer-category exclusion (or plain
    /// spending, for Revolut X), which is exactly the asymmetry that overstated the
    /// dashboard's median monthly savings.
    /// </summary>
    [DbContext(typeof(BankSyncDbContext))]
    [Migration("20260905000000_M013_CounterpartyRuleCoverage")]
    public partial class M013_CounterpartyRuleCoverage : Migration
    {
        private static readonly Guid FamilyMemberId = new("11111111-0000-0000-0000-000000000001");
        private static readonly Guid MariiaId = new("11111111-0000-0000-0000-000000000002");
        private static readonly Guid InvestmentRoutingId = new("11111111-0000-0000-0000-000000000003");
        private static readonly DateTime SeededAt = new(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);

        private static readonly Guid[] RuleIds =
        [
            new("22222222-0000-0000-0000-000000000005"),
            new("22222222-0000-0000-0000-000000000006"),
            new("22222222-0000-0000-0000-000000000015"),
            new("22222222-0000-0000-0000-000000000016")
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "bank_sync",
                table: "counterparty_rules",
                columns: ["Id", "CounterpartyId", "MatchType", "Pattern", "CreatedAt"],
                columnTypes: ["uuid", "uuid", "character varying(50)", "character varying(255)", "timestamp with time zone"],
                values: new object[,]
                {
                    // Latin transliterations used by Revolut/AIB statement lines. Matching is
                    // case-insensitive substring, so "To Mariia M" and standalone
                    // "Oksana Lysenko" both hit.
                    {
                        RuleIds[0],
                        FamilyMemberId,
                        "description_contains",
                        "Oksana Lysenko",
                        SeededAt
                    },
                    {
                        RuleIds[1],
                        MariiaId,
                        "description_contains",
                        "Mariia",
                        SeededAt
                    },
                    // Revolut X is a funding leg to a crypto venue, same role as Binance/IBKR:
                    // money changing sleeve, not spending.
                    {
                        RuleIds[2],
                        InvestmentRoutingId,
                        "description_contains",
                        "Revolut X",
                        SeededAt
                    },
                    {
                        RuleIds[3],
                        InvestmentRoutingId,
                        "merchant_name_contains",
                        "Revolut X",
                        SeededAt
                    }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var ruleId in RuleIds)
            {
                migrationBuilder.DeleteData(
                    schema: "bank_sync",
                    table: "counterparty_rules",
                    keyColumn: "Id",
                    keyColumnType: "uuid",
                    keyValue: ruleId);
            }
        }
    }
}
