namespace FinanceSentry.Modules.Analytics.Tests.Integration;

using FinanceSentry.Modules.Analytics.Application.Queries;
using FinanceSentry.Modules.Analytics.Application.Services;
using FinanceSentry.Modules.Analytics.Domain;
using FinanceSentry.Modules.Analytics.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

/// <summary>
/// The real safety proof (feature 033, SC-002/003/004/006): read-only role, cross-user isolation, and
/// the time/row budget enforced at the database — not by the validator alone. Skips when the
/// integration DB (<c>ANALYTICS_TEST_PG</c>) is unavailable.
/// </summary>
[Collection(AnalyticsPostgresCollection.Name)]
public sealed class ReadOnlyExecutorTests(AnalyticsPostgresFixture fixture)
{
    private readonly AnalyticsPostgresFixture _fx = fixture;

    private ReadOnlyQueryExecutor Executor(int maxRows = 1000, int timeoutMs = 5000)
        => new(Options.Create(new AnalyticsOptions
        {
            ReadOnlyConnectionString = _fx.ConnectionString!,
            MaxRows = maxRows,
            StatementTimeoutMs = timeoutMs,
        }));

    [Fact]
    public async Task Query_ReturnsOnlyTheCallersRows()
    {
        if (!_fx.Available)
        {
            return;
        }

        var executor = Executor();

        var a = await executor.ExecuteAsync(
            AnalyticsPostgresFixture.UserA, "SELECT amount FROM analytics.v_transactions ORDER BY amount");
        var b = await executor.ExecuteAsync(
            AnalyticsPostgresFixture.UserB, "SELECT amount FROM analytics.v_transactions ORDER BY amount");

        a.Rows.Select(r => (decimal)r[0]!).Should().Equal(10m, 20m, 30m);
        b.Rows.Select(r => (decimal)r[0]!).Should().Equal(100m, 200m);
    }

    [Fact]
    public async Task Query_CannotWidenBeyondTheCaller()
    {
        if (!_fx.Available)
        {
            return;
        }

        // Even a deliberately unfiltered query only ever sees the caller's rows — the view filter dominates.
        var result = await Executor().ExecuteAsync(
            AnalyticsPostgresFixture.UserA, "SELECT COUNT(*) AS c FROM analytics.v_transactions WHERE 1=1");

        ((long)result.Rows[0][0]!).Should().Be(3);
    }

    [Fact]
    public async Task ReadOnlyRole_CannotWrite_AndCannotReachBaseTables()
    {
        if (!_fx.Available)
        {
            return;
        }

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using (var setRole = new NpgsqlCommand("SET ROLE fs_readonly;", conn))
        {
            await setRole.ExecuteNonQueryAsync();
        }

        // No write privilege anywhere (FR-002).
        var write = async () =>
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO analytics.query_audit (\"Id\",\"UserId\",\"Sql\",\"Outcome\",\"CreatedAt\") "
                + "VALUES (gen_random_uuid(), gen_random_uuid(), 'x', 'Executed', now());",
                conn);
            await cmd.ExecuteNonQueryAsync();
        };
        (await write.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");

        // Base tables are unreachable — only the curated views are granted (FR-003).
        var reachBaseTable = async () =>
        {
            await using var cmd = new NpgsqlCommand("SELECT 1 FROM bank_sync.\"Transactions\" LIMIT 1;", conn);
            await cmd.ExecuteScalarAsync();
        };
        (await reachBaseTable.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
    }

    [Fact]
    public async Task RunawayQuery_IsStoppedByStatementTimeout()
    {
        if (!_fx.Available)
        {
            return;
        }

        var result = await Executor(timeoutMs: 300).ExecuteAsync(
            AnalyticsPostgresFixture.UserA, "SELECT pg_sleep(3)");

        result.TooLarge.Should().BeTrue();
        result.Reason.Should().Contain("narrow");
    }

    [Fact]
    public async Task LargeResult_IsClippedAtTheRowCap()
    {
        if (!_fx.Available)
        {
            return;
        }

        var result = await Executor(maxRows: 5).ExecuteAsync(
            AnalyticsPostgresFixture.UserA, "SELECT g.n FROM generate_series(1, 50) AS g(n)");

        result.Rows.Should().HaveCount(5);
        result.Truncated.Should().BeTrue();
    }

    [Fact]
    public async Task Handler_AuditsBothExecutedAndRejectedQueries()
    {
        if (!_fx.Available)
        {
            return;
        }

        await using var auditContext = _fx.NewAnalyticsContext();
        var handler = new RunAnalyticsQueryHandler(
            new SqlGuard(),
            Executor(),
            new QueryAuditRepository(auditContext));

        const string executedSql = "SELECT amount FROM analytics.v_transactions";
        const string rejectedSql = "DELETE FROM analytics.query_audit";

        var executed = await handler.Handle(
            new RunAnalyticsQuery(AnalyticsPostgresFixture.UserA, executedSql), CancellationToken.None);
        var rejected = await handler.Handle(
            new RunAnalyticsQuery(AnalyticsPostgresFixture.UserA, rejectedSql), CancellationToken.None);

        executed.Error.Should().BeNull();
        rejected.Error.Should().Be("rejected");

        await using var verify = _fx.NewAnalyticsContext(AnalyticsPostgresFixture.UserA);
        var executedRow = await verify.QueryAudit
            .Where(r => r.Sql == executedSql).OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync();
        var rejectedRow = await verify.QueryAudit
            .Where(r => r.Sql == rejectedSql).OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync();

        executedRow.Should().NotBeNull();
        executedRow!.Outcome.Should().Be(QueryOutcome.Executed);
        executedRow.RowCount.Should().Be(3);
        executedRow.DurationMs.Should().NotBeNull();

        rejectedRow.Should().NotBeNull();
        rejectedRow!.Outcome.Should().Be(QueryOutcome.Rejected);
        rejectedRow.RejectReason.Should().NotBeNullOrEmpty();
    }
}
