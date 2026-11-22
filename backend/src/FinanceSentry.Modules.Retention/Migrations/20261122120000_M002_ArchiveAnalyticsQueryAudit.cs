using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Retention.Migrations
{
    /// <summary>
    /// Retires the removed Analytics module's database objects (the ad-hoc <c>run_analytics_query</c> tool is gone).
    /// <para>
    /// <c>analytics.query_audit</c> is kept, not dropped: it moves to <c>retention.analytics_query_audit_archive</c>
    /// with its rows, and nothing purges it. The five curated <c>analytics.v_*</c> views, the cluster-wide
    /// <c>fs_readonly</c> role and the <c>analytics</c> schema are dropped. Every statement is guarded, so a
    /// database that never had the Analytics objects (a fresh one) passes through unchanged.
    /// </para>
    /// <para>
    /// <c>Down</c> recreates the schema, role, views and grants and moves the archive back to
    /// <c>analytics.query_audit</c> (an empty table if there is no archive). The earlier restriction of
    /// <c>pg_catalog.set_config</c> EXECUTE to the app login stays in place; it is a privilege tightening only.
    /// Retention is migrated after every schema the views read, so both directions find them.
    /// </para>
    /// </summary>
    public partial class M002_ArchiveAnalyticsQueryAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP VIEW IF EXISTS analytics.v_budgets;
DROP VIEW IF EXISTS analytics.v_net_worth_daily;
DROP VIEW IF EXISTS analytics.v_analyst_actions;
DROP VIEW IF EXISTS analytics.v_holdings;
DROP VIEW IF EXISTS analytics.v_transactions;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'fs_readonly') THEN
        EXECUTE 'REVOKE fs_readonly FROM ' || quote_ident(CURRENT_USER);
        IF EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'analytics') THEN
            REVOKE ALL ON SCHEMA analytics FROM fs_readonly;
        END IF;
        DROP ROLE fs_readonly;
    END IF;
    IF to_regclass('analytics.query_audit') IS NOT NULL AND to_regclass('retention.analytics_query_audit_archive') IS NULL THEN
        ALTER TABLE analytics.query_audit SET SCHEMA retention;
        ALTER TABLE retention.query_audit RENAME TO analytics_query_audit_archive;
    END IF;
END
$$;
DROP SCHEMA IF EXISTS analytics;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE SCHEMA IF NOT EXISTS analytics;
DO $$
BEGIN
    IF to_regclass('retention.analytics_query_audit_archive') IS NOT NULL THEN
        ALTER TABLE retention.analytics_query_audit_archive RENAME TO query_audit;
        ALTER TABLE retention.query_audit SET SCHEMA analytics;
    ELSE
        CREATE TABLE analytics.query_audit (
            ""Id"" uuid NOT NULL,
            ""UserId"" uuid NOT NULL,
            ""Sql"" character varying(8000) NOT NULL,
            ""Outcome"" character varying(16) NOT NULL,
            ""RejectReason"" character varying(500),
            ""RowCount"" integer,
            ""DurationMs"" integer,
            ""CreatedAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_query_audit"" PRIMARY KEY (""Id"")
        );
        CREATE INDEX ""IX_query_audit_UserId_CreatedAt"" ON analytics.query_audit (""UserId"", ""CreatedAt"");
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'fs_readonly') THEN
        CREATE ROLE fs_readonly NOLOGIN;
    END IF;
END
$$;
GRANT USAGE ON SCHEMA analytics TO fs_readonly;
GRANT fs_readonly TO CURRENT_USER;
");

            migrationBuilder.Sql(@"
CREATE VIEW analytics.v_transactions WITH (security_barrier = true) AS
SELECT
    COALESCE(t.""PostedDate"", t.""TransactionDate"")::date AS date,
    t.""Amount""          AS amount,
    ba.""Currency""       AS currency,
    t.""MerchantName""    AS merchant,
    t.""MerchantCategory"" AS category,
    ba.""BankName""       AS account_name,
    t.""TransactionType"" AS direction
FROM bank_sync.""Transactions"" t
JOIN bank_sync.""BankAccounts"" ba ON ba.""Id"" = t.""AccountId""
WHERE t.""IsActive""
  AND t.""UserId"" = current_setting('app.current_user_id', true)::uuid;
");

            migrationBuilder.Sql(@"
CREATE VIEW analytics.v_holdings WITH (security_barrier = true) AS
SELECT
    h.""Symbol""         AS symbol,
    h.""InstrumentType"" AS asset_class,
    h.""Quantity""       AS quantity,
    h.""UsdValue""       AS market_value_usd,
    h.""CostBasisUsd""   AS cost_basis_usd,
    h.""Provider""       AS account
FROM brokerage_sync.""BrokerageHoldings"" h
WHERE h.""UserId"" = current_setting('app.current_user_id', true)::uuid
UNION ALL
SELECT
    c.""Asset""                              AS symbol,
    'crypto'                                 AS asset_class,
    (c.""FreeQuantity"" + c.""LockedQuantity"") AS quantity,
    c.""UsdValue""                           AS market_value_usd,
    c.""CostBasisUsd""                       AS cost_basis_usd,
    c.""Provider""                           AS account
FROM crypto_sync.""CryptoHoldings"" c
WHERE c.""UserId"" = current_setting('app.current_user_id', true)::uuid;
");

            migrationBuilder.Sql(@"
CREATE VIEW analytics.v_analyst_actions WITH (security_barrier = true) AS
SELECT
    a.""Ticker""       AS ticker,
    a.""Firm""         AS firm,
    a.""ActionType""   AS action_type,
    a.""PriorRating""  AS prior_rating,
    a.""NewRating""    AS new_rating,
    a.""PriorTarget""  AS prior_target,
    a.""NewTarget""    AS new_target,
    a.""ActionDate""   AS action_date
FROM research.""analyst_actions"" a;
");

            migrationBuilder.Sql(@"
CREATE VIEW analytics.v_net_worth_daily WITH (security_barrier = true) AS
SELECT
    s.""SnapshotDate""   AS as_of_date,
    s.""TotalNetWorth""  AS total_usd,
    s.""BankingTotal""   AS banking_usd,
    s.""BrokerageTotal"" AS brokerage_usd,
    s.""CryptoTotal""    AS crypto_usd
FROM public.""net_worth_snapshots"" s
WHERE s.""UserId"" = current_setting('app.current_user_id', true)::uuid;
");

            migrationBuilder.Sql(@"
CREATE VIEW analytics.v_budgets WITH (security_barrier = true) AS
SELECT
    b.""Category""     AS category,
    'monthly'          AS period,
    b.""MonthlyLimit"" AS limit_amount,
    COALESCE(spend.spent, 0) AS spent_amount,
    (b.""MonthlyLimit"" - COALESCE(spend.spent, 0)) AS remaining
FROM budgets.""budgets"" b
LEFT JOIN LATERAL (
    SELECT SUM(t.""Amount"") AS spent
    FROM bank_sync.""Transactions"" t
    WHERE t.""UserId"" = b.""UserId""
      AND t.""IsActive""
      AND t.""TransactionType"" = 'debit'
      AND t.""MerchantCategory"" = b.""Category""
      AND COALESCE(t.""PostedDate"", t.""TransactionDate"") >= date_trunc('month', CURRENT_DATE)
      AND COALESCE(t.""PostedDate"", t.""TransactionDate"") <  (date_trunc('month', CURRENT_DATE) + INTERVAL '1 month')
) spend ON true
WHERE b.""UserId"" = current_setting('app.current_user_id', true)::uuid;
");

            migrationBuilder.Sql(@"
GRANT SELECT ON
    analytics.v_transactions,
    analytics.v_holdings,
    analytics.v_analyst_actions,
    analytics.v_net_worth_daily,
    analytics.v_budgets
TO fs_readonly;
");
        }
    }
}
