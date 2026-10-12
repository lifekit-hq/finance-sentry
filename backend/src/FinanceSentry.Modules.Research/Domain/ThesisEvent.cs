namespace FinanceSentry.Modules.Research.Domain;

using FinanceSentry.Core.Auth;

/// <summary>
/// Append-only price-stamped lifecycle event for a thesis or candidate (FR-001/FR-009). No row is
/// ever updated after insert except <see cref="SubjectPrice"/>/<see cref="BenchmarkPrice"/>/
/// <see cref="PricesPending"/>, and only by <c>ThesisTrackRecordSnapshotJob</c> backfilling a
/// previously-unknown price — never a correction of a recorded fact.
/// </summary>
public class ThesisEvent : IUserOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public ThesisSubjectType SubjectType { get; set; }

    /// <summary>
    /// FK-by-convention to <see cref="InvestmentThesis"/>.Id (or a future 019 Candidate.Id) — no EF
    /// navigation/FK constraint across the two concepts since Candidate doesn't exist yet.
    /// </summary>
    public Guid SubjectId { get; set; }

    public string Ticker { get; set; } = string.Empty;

    public ThesisEventType EventType { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public decimal? SubjectPrice { get; set; }

    public decimal? BenchmarkPrice { get; set; }

    public string BenchmarkTicker { get; set; } = "SPY";

    public bool PricesPending { get; set; }

    public string? DecisionNote { get; set; }
}
