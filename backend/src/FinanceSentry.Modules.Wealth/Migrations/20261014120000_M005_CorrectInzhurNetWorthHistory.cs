using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Wealth.Migrations
{
    /// <summary>
    /// One-off data correction (money-semantics §1 "Inzhur", §8 "Inzhur history correction"). Inzhur syncs before the kopeck fix
    /// (2026-10-12) stored every Inzhur amount 100x too large, and the net-worth snapshots taken from those holdings
    /// kept the inflated value after the next sync rewrote the holdings. For each such snapshot this copies the row
    /// into <c>net_worth_snapshots_backup_inzhur_2026_10</c> first, then removes the inflated part:
    /// <list type="bullet">
    /// <item>A user is corrected only once a fixed build has synced Inzhur: the first Inzhur ledger row it stored
    /// (the ledger arrived with the fix, in the same sync as the corrected holdings) marks that moment.</item>
    /// <item>An affected snapshot is one taken from the Inzhur connection (and no earlier than 2026-10-12, the first
    /// live Inzhur sync) up to that moment, with a measured, not carried-forward, brokerage sleeve.</item>
    /// <item>The real Inzhur value at the snapshot comes from the corrected data: today's Inzhur holdings rolled back
    /// through the ledger rows dated after the snapshot (units bought or sold since, priced at the holding's corrected
    /// unit value or, for an asset no longer held, at its own trade price; cash less the movements since).</item>
    /// <item>The stored figure held 100 times that value (the unit error, every amount read in kopecks as hryvnia), so
    /// 99 times it comes off the brokerage sleeve and the total, the invested part off <c>brokerage_invested</c> and
    /// the cash part off <c>cash_total</c>. A null split stays null.</item>
    /// </list>
    /// Idempotent: a row whose backup holds a different brokerage figure from the live row is already corrected and is
    /// left alone, so running the SQL again changes nothing. Down puts the original values back from the backup and
    /// keeps the backup table.
    /// </summary>
    public partial class M005_CorrectInzhurNetWorthHistory : Migration
    {
        public const string BackupTable = "net_worth_snapshots_backup_inzhur_2026_10";

        public const string CorrectionSql = """
            CREATE TABLE IF NOT EXISTS net_worth_snapshots_backup_inzhur_2026_10 (
                "Id" uuid PRIMARY KEY,
                "UserId" uuid NOT NULL,
                "SnapshotDate" date NOT NULL,
                "TakenAt" timestamp with time zone NOT NULL,
                "BankingTotal" numeric(18,2) NOT NULL,
                "BrokerageTotal" numeric(18,2) NOT NULL,
                "CryptoTotal" numeric(18,2) NOT NULL,
                "TotalNetWorth" numeric(18,2) NOT NULL,
                cash_total numeric(18,2),
                brokerage_invested numeric(18,2),
                crypto_invested numeric(18,2),
                inzhur_invested_usd numeric(18,4) NOT NULL,
                inzhur_cash_usd numeric(18,4) NOT NULL,
                removed_invested_usd numeric(18,2) NOT NULL,
                removed_cash_usd numeric(18,2) NOT NULL,
                backed_up_at timestamp with time zone NOT NULL DEFAULT now()
            );

            DO $$
            BEGIN
                -- BrokerageSync migrates before Wealth (MigrationExtensions), so these exist on every real database;
                -- the guard only keeps a Wealth-only migration (tests, a design-time database) from failing.
                IF to_regclass('brokerage_sync."InzhurCredentials"') IS NULL
                   OR to_regclass('brokerage_sync."BrokerageHoldings"') IS NULL
                   OR to_regclass('brokerage_sync."BrokerageTrades"') IS NULL
                   OR to_regclass('brokerage_sync."BrokerageCashTransactions"') IS NULL THEN
                    RETURN;
                END IF;

                WITH fixed AS (
                    SELECT l."UserId", min(l."CreatedAt") AS fixed_at
                    FROM (
                        SELECT "UserId", "CreatedAt" FROM brokerage_sync."BrokerageTrades" WHERE "Provider" = 'inzhur'
                        UNION ALL
                        SELECT "UserId", "CreatedAt" FROM brokerage_sync."BrokerageCashTransactions" WHERE "Provider" = 'inzhur'
                    ) l
                    GROUP BY l."UserId"
                ),
                windows AS (
                    SELECT f."UserId", greatest(c."CreatedAt", TIMESTAMPTZ '2026-10-12 00:00:00+00') AS from_at, f.fixed_at
                    FROM fixed f
                    JOIN brokerage_sync."InzhurCredentials" c ON c."UserId" = f."UserId"
                ),
                rates AS (
                    SELECT h."UserId", sum(h."UsdValue") / sum(h."NativeValue") AS usd_per_uah
                    FROM brokerage_sync."BrokerageHoldings" h
                    WHERE h."Provider" = 'inzhur' AND h."Currency" = 'UAH' AND h."NativeValue" > 0
                    GROUP BY h."UserId"
                ),
                candidates AS (
                    SELECT s."Id", s."UserId", s."TakenAt", r.usd_per_uah
                    FROM net_worth_snapshots s
                    JOIN windows w ON w."UserId" = s."UserId"
                    JOIN rates r ON r."UserId" = s."UserId"
                    LEFT JOIN net_worth_snapshots_backup_inzhur_2026_10 b ON b."Id" = s."Id"
                    WHERE s."TakenAt" >= w.from_at
                      AND s."TakenAt" < w.fixed_at
                      AND NOT s.is_approximate
                      AND coalesce(s.stale_sleeves, '') NOT LIKE '%brokerage%'
                      AND (b."Id" IS NULL OR b."BrokerageTotal" = s."BrokerageTotal")
                ),
                valued AS (
                    SELECT c."Id",
                        greatest(0,
                            (SELECT coalesce(sum(h."UsdValue"), 0)
                             FROM brokerage_sync."BrokerageHoldings" h
                             WHERE h."UserId" = c."UserId" AND h."Provider" = 'inzhur' AND h."InstrumentType" <> 'CASH')
                          - (SELECT coalesce(sum(t."Quantity" * coalesce(h."UsdValue" / nullif(h."Quantity", 0), t."Price" * c.usd_per_uah)), 0)
                             FROM brokerage_sync."BrokerageTrades" t
                             LEFT JOIN brokerage_sync."BrokerageHoldings" h
                               ON h."UserId" = t."UserId" AND h."Provider" = 'inzhur' AND h."Symbol" = t."Symbol"
                              AND h."InstrumentType" <> 'CASH'
                             WHERE t."UserId" = c."UserId" AND t."Provider" = 'inzhur' AND t."TradeDateTime" > c."TakenAt")
                        ) AS inzhur_invested_usd,
                        greatest(0,
                            (SELECT coalesce(sum(h."Quantity"), 0)
                             FROM brokerage_sync."BrokerageHoldings" h
                             WHERE h."UserId" = c."UserId" AND h."Provider" = 'inzhur' AND h."InstrumentType" = 'CASH')
                          - (SELECT coalesce(sum(x."Amount"), 0)
                             FROM brokerage_sync."BrokerageCashTransactions" x
                             WHERE x."UserId" = c."UserId" AND x."Provider" = 'inzhur' AND x."DateTime" > c."TakenAt")
                          - (SELECT coalesce(sum(t."Proceeds"), 0)
                             FROM brokerage_sync."BrokerageTrades" t
                             WHERE t."UserId" = c."UserId" AND t."Provider" = 'inzhur' AND t."TradeDateTime" > c."TakenAt")
                        ) * c.usd_per_uah AS inzhur_cash_usd
                    FROM candidates c
                ),
                removal AS (
                    SELECT v."Id", v.inzhur_invested_usd, v.inzhur_cash_usd,
                        round(99 * v.inzhur_invested_usd, 2) AS removed_invested_usd,
                        round(99 * v.inzhur_cash_usd, 2) AS removed_cash_usd
                    FROM valued v
                ),
                correction AS (
                    SELECT r.*
                    FROM removal r
                    JOIN net_worth_snapshots s ON s."Id" = r."Id"
                    WHERE r.removed_invested_usd + r.removed_cash_usd > 0
                      AND s."BrokerageTotal" >= r.removed_invested_usd + r.removed_cash_usd
                      AND (s.brokerage_invested IS NULL OR s.brokerage_invested >= r.removed_invested_usd)
                ),
                backed_up AS (
                    INSERT INTO net_worth_snapshots_backup_inzhur_2026_10 (
                        "Id", "UserId", "SnapshotDate", "TakenAt", "BankingTotal", "BrokerageTotal", "CryptoTotal",
                        "TotalNetWorth", cash_total, brokerage_invested, crypto_invested,
                        inzhur_invested_usd, inzhur_cash_usd, removed_invested_usd, removed_cash_usd)
                    SELECT s."Id", s."UserId", s."SnapshotDate", s."TakenAt", s."BankingTotal", s."BrokerageTotal",
                        s."CryptoTotal", s."TotalNetWorth", s.cash_total, s.brokerage_invested, s.crypto_invested,
                        c.inzhur_invested_usd, c.inzhur_cash_usd, c.removed_invested_usd, c.removed_cash_usd
                    FROM net_worth_snapshots s
                    JOIN correction c ON c."Id" = s."Id"
                    -- Only after a Down: the stored originals already equal the live row, the derivation is refreshed.
                    ON CONFLICT ("Id") DO UPDATE SET
                        inzhur_invested_usd = EXCLUDED.inzhur_invested_usd,
                        inzhur_cash_usd = EXCLUDED.inzhur_cash_usd,
                        removed_invested_usd = EXCLUDED.removed_invested_usd,
                        removed_cash_usd = EXCLUDED.removed_cash_usd,
                        backed_up_at = now()
                    RETURNING "Id"
                )
                UPDATE net_worth_snapshots s SET
                    "BrokerageTotal" = s."BrokerageTotal" - c.removed_invested_usd - c.removed_cash_usd,
                    "TotalNetWorth" = s."TotalNetWorth" - c.removed_invested_usd - c.removed_cash_usd,
                    brokerage_invested = s.brokerage_invested - c.removed_invested_usd,
                    cash_total = s.cash_total - c.removed_cash_usd
                FROM correction c
                JOIN backed_up b ON b."Id" = c."Id"
                WHERE s."Id" = c."Id";
            END $$;
            """;

        public const string RestoreSql = """
            UPDATE net_worth_snapshots s SET
                "BrokerageTotal" = b."BrokerageTotal",
                "TotalNetWorth" = b."TotalNetWorth",
                cash_total = b.cash_total,
                brokerage_invested = b.brokerage_invested
            FROM net_worth_snapshots_backup_inzhur_2026_10 b
            WHERE b."Id" = s."Id";
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(CorrectionSql);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(RestoreSql);
    }
}
