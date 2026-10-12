namespace FinanceSentry.Modules.Analytics.Domain;

using FinanceSentry.Core.Auth;

/// <summary>
/// One executed or rejected analytics query (feature 033, US3). Written on the app's normal writable
/// connection — NOT the read-only connection the query itself runs on — so the audit trail is durable
/// even though the query path has no write privilege.
/// </summary>
public sealed class QueryAuditRecord : IUserOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The caller whose identity scoped the query.</summary>
    public Guid UserId { get; set; }

    /// <summary>The exact statement the agent submitted.</summary>
    public string Sql { get; set; } = string.Empty;

    public QueryOutcome Outcome { get; set; }

    /// <summary>Populated when <see cref="Outcome"/> is <see cref="QueryOutcome.Rejected"/> (or execution failed).</summary>
    public string? RejectReason { get; set; }

    /// <summary>Rows returned when executed (after the row cap).</summary>
    public int? RowCount { get; set; }

    /// <summary>Wall-clock execution time in milliseconds when executed.</summary>
    public int? DurationMs { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
