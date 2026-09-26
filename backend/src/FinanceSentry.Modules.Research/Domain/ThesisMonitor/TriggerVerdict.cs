namespace FinanceSentry.Modules.Research.Domain.ThesisMonitor;

/// <summary>
/// Outcome of evaluating a single <see cref="ThesisInvalidationTrigger"/> against fundamentals
/// and/or price history. Modeled as a closed abstract record hierarchy so consumers can pattern-match
/// exhaustively without a separate discriminator field.
/// </summary>
public abstract record TriggerVerdict
{
    public sealed record Breached(
        string Metric,
        decimal[] ObservedValues,
        string[] Periods,
        decimal Threshold,
        string Direction) : TriggerVerdict;

    public sealed record Held : TriggerVerdict;

    public sealed record NonEvaluable(string Reason) : TriggerVerdict;
}

/// <summary>
/// Closed set of reasons a trigger could not be evaluated (never a breach).
/// </summary>
public static class NonEvaluableReason
{
    public const string NoFundamentals = "no_fundamentals";
    public const string InsufficientPeriods = "insufficient_periods";
    public const string DivideByZero = "divide_by_zero";
    public const string NoPriceHistory = "no_price_history";
    public const string UnsupportedMetric = "unsupported_metric";
    public const string InvalidDirection = "invalid_direction";
    public const string InvalidConsecutivePeriods = "invalid_consecutive_periods";
    public const string MissingBenchmarkConfiguration = "missing_benchmark_configuration";
    public const string NoBenchmarkHistory = "no_benchmark_history";
}
