using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.BankSync.Migrations
{
    /// <summary>
    /// Seeds the self-routing counterparty for the user's own EUR accounts (AIB ↔ Revolut) and
    /// retires the direction-blind merchant keyword that used to classify one of its legs.
    ///
    /// <para>
    /// The hop shows as <c>*MOBI DENYS SYCHOV IE…</c> on the AIB side and
    /// <c>Payment from Denys Sychov</c> on the Revolut side. Pair detection cannot see them as a
    /// pair — the statements share no words beyond the name and the legs settle on different
    /// days — so the Revolut credit read as INCOME and inflated both gross income and the
    /// savings rate, month after month.
    /// </para>
    ///
    /// <para>
    /// The keyword <c>denys sychov ie</c> → <c>TRANSFER_IN</c> was the earlier hand-made patch
    /// for the inbound leg. The keyword bridge is the top rung of the categorization ladder and
    /// carries no direction, so it stamped TRANSFER_IN on the OUTBOUND leg too — an outgoing
    /// debit filed as an incoming transfer. The counterparty layer classifies per direction and
    /// now owns both legs, so the keyword is removed rather than left as a landmine over any
    /// future own-name movement.
    /// </para>
    /// </summary>
    [DbContext(typeof(BankSyncDbContext))]
    [Migration("20260909180000_M019_OwnAccountSelfRouting")]
    public partial class M019_OwnAccountSelfRouting : Migration
    {
        private static readonly Guid OwnAccountsId = new("11111111-0000-0000-0000-000000000006");
        private static readonly Guid RuleId = new("22222222-0000-0000-0000-000000000020");
        private static readonly DateTime SeededAt = new(2026, 9, 9, 18, 0, 0, DateTimeKind.Utc);

        private const string SupersededKeyword = "denys sychov ie";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "bank_sync",
                table: "counterparties",
                columns: ["Id", "UserId", "Name", "FlowRole", "CreatedAt"],
                columnTypes: ["uuid", "uuid", "character varying(255)", "character varying(50)", "timestamp with time zone"],
                values: new object[] { OwnAccountsId, Guid.Empty, "Own accounts (EUR)", "self_routing", SeededAt });

            migrationBuilder.InsertData(
                schema: "bank_sync",
                table: "counterparty_rules",
                columns: ["Id", "CounterpartyId", "MatchType", "Pattern", "Currency", "CreatedAt"],
                columnTypes: ["uuid", "uuid", "character varying(50)", "character varying(255)", "character varying(3)", "timestamp with time zone"],
                // One rule covers both legs: the AIB debit ("*MOBI DENYS SYCHOV IE…", "DENYS
                // SYCHOV IE… Sent from Revolut") and the Revolut credit ("Payment from Denys
                // Sychov"). EUR-scoped like the mom-routing rules (#580), so a Cyrillic UAH
                // statement carrying the same name keeps matching the family rules instead.
                values: new object[] { RuleId, OwnAccountsId, "description_contains", "Denys Sychov", "EUR", SeededAt });

            migrationBuilder.DeleteData(
                schema: "bank_sync",
                table: "merchant_keywords",
                keyColumn: "Keyword",
                keyColumnType: "character varying(100)",
                keyValue: SupersededKeyword);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "bank_sync",
                table: "merchant_keywords",
                columns: ["Keyword", "CategoryKey"],
                columnTypes: ["character varying(100)", "character varying(100)"],
                values: new object[] { SupersededKeyword, "TRANSFER_IN" });

            migrationBuilder.DeleteData(
                schema: "bank_sync",
                table: "counterparty_rules",
                keyColumn: "Id",
                keyColumnType: "uuid",
                keyValue: RuleId);

            migrationBuilder.DeleteData(
                schema: "bank_sync",
                table: "counterparties",
                keyColumn: "Id",
                keyColumnType: "uuid",
                keyValue: OwnAccountsId);
        }
    }
}
