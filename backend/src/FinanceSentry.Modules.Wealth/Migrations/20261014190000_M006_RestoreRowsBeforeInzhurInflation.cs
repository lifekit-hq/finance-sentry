using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Wealth.Migrations
{
    /// <summary>
    /// Follow-up to <see cref="M005_CorrectInzhurNetWorthHistory"/> (money-semantics §8, "Inzhur history correction").
    /// M005 derived the inflated Inzhur value from the ledger and removed 99x of it from every snapshot taken since the
    /// Inzhur credential was created, but the owner's rows before the first inflated sync never held any Inzhur value
    /// (the connection existed, its holdings had not synced yet), so that removal was a false dip. This puts the
    /// original values back, from <c>net_worth_snapshots_backup_inzhur_2026_10</c> and matched by <c>Id</c>, for the
    /// rows M005 changed that predate the inflation.
    /// <para>
    /// Rule (evidence, not a date list): per user, take the backed-up rows in <c>(SnapshotDate, TakenAt)</c> order and
    /// find the first one whose original <c>BrokerageTotal</c> stepped up from the previous snapshot's (the row before
    /// it as it stood before M005: its backup when it has one, its live value otherwise) by at least half of what M005
    /// removed from it. That row is the first snapshot taken from inflated Inzhur holdings: a genuine 100x Inzhur
    /// value arrives as one jump of about that size, market moves between two days do not. Every backed-up row
    /// before it is restored; it and the rows after it keep the correction (a carried row after the onset is a copy of
    /// an inflated one). A user with no such step has nothing restored, and a backed-up first-ever snapshot has no
    /// previous row to show it was not inflated, so it counts as the onset: when the evidence is missing the M005
    /// correction stays.
    /// </para>
    /// <para>
    /// A row is restored only while it still holds exactly what M005 left (original minus the removal recorded in the
    /// backup); a row changed since is left alone. Idempotent: a restored row no longer matches, so running the SQL
    /// again changes nothing. The backup table is kept and nothing is deleted. Down applies M005's recorded removal
    /// again to exactly the rows the same rule selects, while they still hold their original values.
    /// </para>
    /// </summary>
    public partial class M006_RestoreRowsBeforeInzhurInflation : Migration
    {
        /// <summary>Share of M005's removal that the first inflated snapshot's brokerage step must reach.</summary>
        private const string OnsetStepShare = "0.5";

        private const string RestorableRows = $"""
            WITH ordered AS (
                SELECT s."Id", s."UserId",
                    row_number() OVER w AS position,
                    b."Id" IS NOT NULL AS backed_up,
                    coalesce(b."BrokerageTotal", s."BrokerageTotal") AS original_brokerage,
                    lag(coalesce(b."BrokerageTotal", s."BrokerageTotal")) OVER w AS previous_brokerage,
                    b.removed_invested_usd + b.removed_cash_usd AS removed_total
                FROM net_worth_snapshots s
                LEFT JOIN net_worth_snapshots_backup_inzhur_2026_10 b ON b."Id" = s."Id"
                WINDOW w AS (PARTITION BY s."UserId" ORDER BY s."SnapshotDate", s."TakenAt")
            ),
            onset AS (
                SELECT o."UserId", min(o.position) AS position
                FROM ordered o
                WHERE o.backed_up
                  AND (o.previous_brokerage IS NULL
                       OR o.original_brokerage - o.previous_brokerage >= {OnsetStepShare} * o.removed_total)
                GROUP BY o."UserId"
            ),
            restorable AS (
                SELECT o."Id"
                FROM ordered o
                JOIN onset n ON n."UserId" = o."UserId" AND o.position < n.position
                WHERE o.backed_up
            )
            """;

        public const string RestoreSql = $"""
            {RestorableRows}
            UPDATE net_worth_snapshots s SET
                "BrokerageTotal" = b."BrokerageTotal",
                "TotalNetWorth" = b."TotalNetWorth",
                cash_total = b.cash_total,
                brokerage_invested = b.brokerage_invested
            FROM net_worth_snapshots_backup_inzhur_2026_10 b
            JOIN restorable r ON r."Id" = b."Id"
            WHERE s."Id" = b."Id"
              AND s."BrokerageTotal" = b."BrokerageTotal" - b.removed_invested_usd - b.removed_cash_usd;
            """;

        public const string ReapplyRemovalSql = $"""
            {RestorableRows}
            UPDATE net_worth_snapshots s SET
                "BrokerageTotal" = b."BrokerageTotal" - b.removed_invested_usd - b.removed_cash_usd,
                "TotalNetWorth" = b."TotalNetWorth" - b.removed_invested_usd - b.removed_cash_usd,
                cash_total = b.cash_total - b.removed_cash_usd,
                brokerage_invested = b.brokerage_invested - b.removed_invested_usd
            FROM net_worth_snapshots_backup_inzhur_2026_10 b
            JOIN restorable r ON r."Id" = b."Id"
            WHERE s."Id" = b."Id"
              AND s."BrokerageTotal" = b."BrokerageTotal";
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(RestoreSql);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(ReapplyRemovalSql);
    }
}
