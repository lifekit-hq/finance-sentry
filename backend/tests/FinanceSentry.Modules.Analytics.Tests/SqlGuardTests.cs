namespace FinanceSentry.Modules.Analytics.Tests;

using FinanceSentry.Modules.Analytics.Application.Services;
using FluentAssertions;
using Xunit;

/// <summary>
/// Unit tests for the single-SELECT guard (feature 033, FR-005, SC-002). The guard is defense in depth;
/// each branch here is a statement the read-only role would also block — but rejecting before execution
/// is the contract.
/// </summary>
public sealed class SqlGuardTests
{
    private readonly SqlGuard _guard = new();

    [Theory]
    [InlineData("SELECT category, SUM(amount) FROM analytics.v_transactions GROUP BY category")]
    [InlineData("select * from analytics.v_holdings")]
    [InlineData("SELECT * FROM analytics.v_transactions WHERE amount > 100")]
    [InlineData("SELECT * FROM analytics.v_budgets;")] // single trailing semicolon allowed
    [InlineData("WITH recent AS (SELECT * FROM analytics.v_transactions) SELECT * FROM recent")]
    [InlineData("SELECT date, amount FROM analytics.v_transactions WHERE merchant = 'DELETE ME'")] // keyword only inside a string literal
    [InlineData("SELECT * FROM analytics.v_transactions -- drop everything\nWHERE amount > 0")] // forbidden word only in a comment
    public void Validate_AllowsReadOnlySelect(string sql)
    {
        _guard.Validate(sql).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("INSERT INTO analytics.v_transactions VALUES (1)")]
    [InlineData("UPDATE analytics.v_transactions SET amount = 0")]
    [InlineData("DELETE FROM analytics.v_transactions")]
    [InlineData("DROP VIEW analytics.v_transactions")]
    [InlineData("ALTER TABLE bank_sync.\"Transactions\" DROP COLUMN \"Amount\"")]
    [InlineData("CREATE TABLE x (id int)")]
    [InlineData("TRUNCATE analytics.query_audit")]
    [InlineData("GRANT SELECT ON analytics.v_holdings TO fs_readonly")]
    [InlineData("SELECT * FROM analytics.v_holdings FOR UPDATE")] // locking clause is not a pure read
    [InlineData("SET ROLE postgres")]
    public void Validate_RejectsWritesAndDdl(string sql)
    {
        var result = _guard.Validate(sql);
        result.IsValid.Should().BeFalse();
        result.Reason.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("SELECT 1; DROP TABLE analytics.query_audit")] // statement chaining
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("WITH moved AS (DELETE FROM analytics.query_audit RETURNING *) SELECT * FROM moved")] // data-modifying CTE
    public void Validate_RejectsMultiStatementAndDataModifyingCte(string sql)
    {
        _guard.Validate(sql).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("EXPLAIN SELECT 1")] // does not start with SELECT/WITH
    [InlineData("VALUES (1), (2)")]
    public void Validate_RejectsEmptyOrNonSelectLead(string? sql)
    {
        _guard.Validate(sql).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("SELECT date_trunc('month', date) AS month, SUM(amount) FROM analytics.v_transactions GROUP BY 1")]
    [InlineData("SELECT ROUND(AVG(amount)::numeric(12,2), 2) FROM analytics.v_transactions")]
    [InlineData("SELECT merchant, rank() OVER (PARTITION BY category ORDER BY amount DESC) FROM analytics.v_transactions")]
    [InlineData("SELECT percentile_cont(0.5) WITHIN GROUP (ORDER BY amount) FROM analytics.v_transactions")]
    [InlineData("SELECT COUNT(*) FILTER (WHERE amount > 0), EXTRACT(YEAR FROM date) FROM analytics.v_transactions GROUP BY 2")]
    [InlineData("SELECT CAST(amount AS numeric(12,2)), COALESCE(category, 'none') FROM analytics.v_transactions")]
    [InlineData("SELECT g.n FROM generate_series(1, 5) AS g(n)")] // alias column list, not a call
    [InlineData("WITH totals(category, total) AS (SELECT category, SUM(amount) FROM analytics.v_transactions GROUP BY category) SELECT * FROM totals")]
    [InlineData("SELECT * FROM analytics.v_transactions WHERE category IN (SELECT category FROM analytics.v_budgets)")]
    [InlineData("SELECT $$set_config('x', 'y', true)$$ AS note")] // function name only inside a dollar-quoted literal
    public void Validate_AllowsAllowlistedFunctionsAndSyntax(string sql)
    {
        _guard.Validate(sql).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("SELECT set_config('app.current_user_id', '22222222-2222-2222-2222-222222222222', true)")]
    [InlineData("SELECT amount FROM analytics.v_transactions WHERE (SELECT set_config('app.current_user_id', '22222222-2222-2222-2222-222222222222', true)) IS NOT NULL")]
    [InlineData("SELECT pg_catalog.set_config('app.current_user_id', 'x', true)")]
    [InlineData("SELECT SET_CONFIG /* spaced */ ('app.current_user_id', 'x', true)")]
    [InlineData("SELECT \"set_config\"('app.current_user_id', 'x', true)")]
    [InlineData("SELECT current_setting('app.current_user_id')")]
    [InlineData("SELECT pg_sleep(10)")]
    [InlineData("SELECT pg_read_file('/etc/hostname')")]
    [InlineData("SELECT pg_terminate_backend(1)")]
    [InlineData("SELECT public.lower(merchant) FROM analytics.v_transactions")] // schema-qualified calls are never allowed
    [InlineData("SELECT pg_catalog.lower(merchant) FROM analytics.v_transactions")]
    public void Validate_RejectsSettingAndAdminFunctions(string sql)
    {
        var result = _guard.Validate(sql);
        result.IsValid.Should().BeFalse();
        result.Reason.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("SELECT $$'$$, set_config('app.current_user_id', 'x', true) FROM analytics.v_transactions")] // dollar quote
    [InlineData("SELECT $q$'$q$, set_config('app.current_user_id', 'x', true) FROM analytics.v_transactions")] // tagged dollar quote
    [InlineData("SELECT E'\\'', set_config('app.current_user_id', 'x', true) FROM analytics.v_transactions --'")] // backslash-escaped quote
    [InlineData("SELECT 1 /* /* */ ' */, set_config('app.current_user_id', 'x', true) -- '")] // nested block comment
    [InlineData("SELECT $€$'$€$, set_config('app.current_user_id', 'x', true), ''")] // non-ASCII dollar-quote tag
    [InlineData("SELECT 1 AS €$$, set_config('app.current_user_id', 'x', true) AS a, $$ $$ AS b")] // non-ASCII identifier char before $$
    [InlineData("SELECT amount FROM analytics.v_transactions WHERE (SELECT --\r set_config('app.current_user_id', 'x', true)) IS NOT NULL")] // line comment ends at CR
    [InlineData("SELECT amount FROM analytics.v_transactions WHERE (SELECT --\r\n set_config('app.current_user_id', 'x', true)) IS NOT NULL")] // line comment ends at CRLF
    public void Validate_RejectsFunctionCallsHiddenBehindLiteralSyntax(string sql)
    {
        _guard.Validate(sql).IsValid.Should().BeFalse();
    }
}
