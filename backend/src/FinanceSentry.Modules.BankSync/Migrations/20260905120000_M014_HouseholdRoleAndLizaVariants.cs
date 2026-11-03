using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.BankSync.Migrations
{
    /// <summary>
    /// Data-only migration: two classification gaps found while reconciling the median
    /// monthly savings against the bank statements (issue #573). The recurring mortgage
    /// payment goes card-to-card, so Monobank's MCC lands it as TRANSFER_OUT and it
    /// vanished from outflow — a new "household" counterparty catches it by the target
    /// card mask. And transfers to Mariia appear under transliteration variants
    /// ("Mariya", "Марія К.") that the seeded patterns missed, so real support
    /// was excluded as a transfer.
    /// </summary>
    [DbContext(typeof(BankSyncDbContext))]
    [Migration("20260905120000_M014_HouseholdRoleAndLizaVariants")]
    public partial class M014_HouseholdRoleAndLizaVariants : Migration
    {
        private static readonly Guid MariiaId = new("11111111-0000-0000-0000-000000000002");
        private static readonly Guid MortgageId = new("11111111-0000-0000-0000-000000000004");
        private static readonly DateTime SeededAt = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

        private static readonly Guid[] RuleIds =
        [
            new("22222222-0000-0000-0000-000000000007"),
            new("22222222-0000-0000-0000-000000000008"),
            new("22222222-0000-0000-0000-000000000017")
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "bank_sync",
                table: "counterparties",
                columns: ["Id", "UserId", "Name", "FlowRole", "CreatedAt"],
                columnTypes: ["uuid", "uuid", "character varying(255)", "character varying(50)", "timestamp with time zone"],
                values: new object[] { MortgageId, Guid.Empty, "Mortgage", "household", SeededAt });

            migrationBuilder.InsertData(
                schema: "bank_sync",
                table: "counterparty_rules",
                columns: ["Id", "CounterpartyId", "MatchType", "Pattern", "CreatedAt"],
                columnTypes: ["uuid", "uuid", "character varying(50)", "character varying(255)", "timestamp with time zone"],
                values: new object[,]
                {
                    // Latin statement lines spell her with a "y" ("Mariya Koval"),
                    // Monobank sometimes abbreviates to "Марія К." — cover both stems.
                    {
                        RuleIds[0],
                        MariiaId,
                        "description_contains",
                        "Mariya",
                        SeededAt
                    },
                    {
                        RuleIds[1],
                        MariiaId,
                        "description_contains",
                        "Марія",
                        SeededAt
                    },
                    // The mortgage leaves as a card-to-card payment; the masked target card
                    // number is the only stable token on the statement line.
                    {
                        RuleIds[2],
                        MortgageId,
                        "description_contains",
                        "400000******0002",
                        SeededAt
                    }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rules first — they carry the FK to the counterparty.
            foreach (var ruleId in RuleIds)
            {
                migrationBuilder.DeleteData(
                    schema: "bank_sync",
                    table: "counterparty_rules",
                    keyColumn: "Id",
                    keyColumnType: "uuid",
                    keyValue: ruleId);
            }

            migrationBuilder.DeleteData(
                schema: "bank_sync",
                table: "counterparties",
                keyColumn: "Id",
                keyColumnType: "uuid",
                keyValue: MortgageId);
        }
    }
}
