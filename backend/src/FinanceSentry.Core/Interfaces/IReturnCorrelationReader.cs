namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Core-facing read of a ticker's 63-day daily-return correlation against the book's top holdings and
/// against the book as a whole, computed from the bars Radar already stores (no new data source).
/// Deterministic, no model call. A reference the bars cannot support degrades to "not evaluable" with
/// a reason; a number is never invented. Reusable by the scorecard and, later, by the coverage entity.
/// </summary>
public interface IReturnCorrelationReader
{
    /// <summary>
    /// Correlates <paramref name="ticker"/> against the <paramref name="topHoldings"/> largest entries of
    /// <paramref name="book"/> and against the whole book. The ticker itself is excluded from both references
    /// (a held name is not "correlated to the book" through itself).
    /// </summary>
    /// <param name="book">The book's positions; <see cref="CorrelationReference.Weight"/> is the position's value (any consistent unit).</param>
    /// <param name="topHoldings">How many of the largest positions are reported individually.</param>
    /// <param name="minBookCoverage">Fraction (0-1) of the book's weight that must have usable bars for the book figure to be evaluable.</param>
    Task<TickerCorrelation> GetAsync(
        string ticker,
        IReadOnlyCollection<CorrelationReference> book,
        int topHoldings,
        decimal minBookCoverage,
        CancellationToken ct = default);
}

/// <summary>One book position: its symbol and value (the weight the book return series uses).</summary>
public sealed record CorrelationReference(string Symbol, decimal Weight);

/// <summary>
/// The ticker's correlation with one holding. <see cref="Weight"/> is the holding's fraction of the book
/// (excluding the ticker). <see cref="Correlation"/> is null with a <see cref="NotEvaluableReason"/> when it cannot be computed.
/// </summary>
public sealed record ReferenceCorrelation(
    string Symbol,
    decimal Weight,
    decimal? Correlation,
    int Observations,
    string? NotEvaluableReason);

/// <summary>
/// The ticker's correlation with the weight-averaged daily return of the book. <see cref="Coverage"/> is the
/// fraction of book weight whose bars were usable.
/// </summary>
public sealed record BookCorrelation(
    decimal? Correlation,
    int Observations,
    decimal Coverage,
    string? NotEvaluableReason);

public sealed record TickerCorrelation(
    string Ticker,
    int WindowDays,
    IReadOnlyList<ReferenceCorrelation> TopHoldings,
    BookCorrelation Book);

/// <summary>Reasons a correlation is not evaluable; stable strings so callers and tests can match on them.</summary>
public static class CorrelationReasons
{
    public const string NoBars = "no_bars";
    public const string InsufficientOverlap = "insufficient_overlap";
    public const string ZeroVariance = "zero_variance";
    public const string EmptyBook = "empty_book";
    public const string BookCoverageTooLow = "book_coverage_too_low";
}
