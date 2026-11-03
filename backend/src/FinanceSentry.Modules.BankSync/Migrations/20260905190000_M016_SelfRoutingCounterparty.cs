using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.BankSync.Migrations
{
    /// <summary>
    /// Adds an optional account-currency filter to counterparty rules and seeds the
    /// self-routing counterparty for the monthly Revolut → mom → Monobank EUR hop (#580).
    /// The same statement wording means two different things depending on the account
    /// currency: «Від: Оксана Лисенко» in UAH is rent (income), in EUR it is the user's own
    /// money coming back mid-route — pair detection cannot see the legs as a pair because
    /// the Latin and Cyrillic statements share no words.
    /// </summary>
    [DbContext(typeof(BankSyncDbContext))]
    [Migration("20260905190000_M016_SelfRoutingCounterparty")]
    public partial class M016_SelfRoutingCounterparty : Migration
    {
        private static readonly Guid RoutingId = new("11111111-0000-0000-0000-000000000005");
        private static readonly DateTime SeededAt = new(2026, 9, 5, 19, 0, 0, DateTimeKind.Utc);

        private static readonly Guid[] RuleIds =
        [
            new("22222222-0000-0000-0000-000000000018"),
            new("22222222-0000-0000-0000-000000000019")
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                schema: "bank_sync",
                table: "counterparty_rules",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.InsertData(
                schema: "bank_sync",
                table: "counterparties",
                columns: ["Id", "UserId", "Name", "FlowRole", "CreatedAt"],
                columnTypes: ["uuid", "uuid", "character varying(255)", "character varying(50)", "timestamp with time zone"],
                values: new object[] { RoutingId, Guid.Empty, "Routing via mom (EUR)", "self_routing", SeededAt });

            migrationBuilder.InsertData(
                schema: "bank_sync",
                table: "counterparty_rules",
                columns: ["Id", "CounterpartyId", "MatchType", "Pattern", "Currency", "CreatedAt"],
                columnTypes: ["uuid", "uuid", "character varying(50)", "character varying(255)", "character varying(3)", "timestamp with time zone"],
                values: new object[,]
                {
                    // The inbound leg (Monobank EUR credit) and the outbound leg (Revolut EUR
                    // debit). EUR-scoped so the same wordings on UAH accounts keep matching
                    // the generic family rules (rent stays income, support stays spending).
                    {
                        RuleIds[0],
                        RoutingId,
                        "description_contains",
                        "Від: Оксана Лисенко",
                        "EUR",
                        SeededAt
                    },
                    {
                        RuleIds[1],
                        RoutingId,
                        "description_contains",
                        "Oksana Lysenko",
                        "EUR",
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

            migrationBuilder.DeleteData(
                schema: "bank_sync",
                table: "counterparties",
                keyColumn: "Id",
                keyColumnType: "uuid",
                keyValue: RoutingId);

            migrationBuilder.DropColumn(
                name: "Currency",
                schema: "bank_sync",
                table: "counterparty_rules");
        }
    }
}
