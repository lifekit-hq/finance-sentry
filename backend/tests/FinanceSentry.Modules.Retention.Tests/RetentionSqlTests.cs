namespace FinanceSentry.Modules.Retention.Tests;

using FinanceSentry.Modules.Retention.Application.Services;
using FinanceSentry.Modules.Retention.Domain;
using FluentAssertions;
using Xunit;

/// <summary>
/// The generic purge statement shape (feature 024, US1): exact-case quoted identifiers, a bound cutoff
/// parameter (no injection), and bounded <c>ctid</c> batch deletes for idempotent, lock-friendly purges.
/// </summary>
public sealed class RetentionSqlTests
{
    // Mixed-case on purpose: companion_events is snake, SyncJobs is Pascal — both must be quoted verbatim.
    private static readonly RetentionPolicy Snake =
        new("companion", "companion_events", "CapturedAt", RetentionAction.Purge, 90);
    private static readonly RetentionPolicy Pascal =
        new("bank_sync", "SyncJobs", "CreatedAt", RetentionAction.Purge, 90);

    [Fact]
    public void Count_sql_quotes_identifiers_and_binds_cutoff()
    {
        RetentionSql.Count(Snake).Should().Be(
            "SELECT COUNT(*)::bigint FROM \"companion\".\"companion_events\" WHERE \"CapturedAt\" < @cutoff");
    }

    [Fact]
    public void Purge_sql_uses_ctid_batch_with_limit_and_bound_cutoff()
    {
        var sql = RetentionSql.PurgeBatch(Pascal, 500);

        sql.Should().Be(
            "DELETE FROM \"bank_sync\".\"SyncJobs\" WHERE ctid IN " +
            "(SELECT ctid FROM \"bank_sync\".\"SyncJobs\" WHERE \"CreatedAt\" < @cutoff LIMIT 500)");
    }

    [Fact]
    public void Purge_sql_never_inlines_the_cutoff_value()
    {
        // The only literal permitted is the compiled batch size; the time cutoff is always a parameter.
        RetentionSql.PurgeBatch(Snake, 1000).Should().Contain("@cutoff").And.NotContain("'");
    }

    [Fact]
    public void Quoted_helpers_use_exact_case()
    {
        Pascal.QuotedTable.Should().Be("\"bank_sync\".\"SyncJobs\"");
        Pascal.QuotedTimestamp.Should().Be("\"CreatedAt\"");
        Pascal.QualifiedName.Should().Be("bank_sync.SyncJobs");
    }
}
