namespace FinanceSentry.Core.Measures;

/// <summary>
/// Why a measure has no number. A failure is a typed reason: never an exception and never a zero
/// (finance-framework §3.4, "Failures are typed").
/// </summary>
public enum MeasureFailure
{
    /// <summary>The base is zero: a window starting at a value of zero (a new account) has no percent.</summary>
    ZeroBase,

    /// <summary>The base, or the value on some day, is negative (a margin deficit): a percent of it has no meaning.</summary>
    NegativeBase,

    /// <summary>The set of accounts changed during the window, so a percent change would count linking as growth.</summary>
    ScopeChanged,

    /// <summary>XIRR: the cash flows are all of one sign, so no rate makes their present value zero.</summary>
    NoSignChange,

    /// <summary>XIRR: the root finder found no rate within its iteration limits.</summary>
    NoConvergence,

    /// <summary>The opening or closing valuation of the window is missing.</summary>
    MissingValue,

    /// <summary>Fewer than two valuations, or a window that does not move forward in time.</summary>
    InsufficientData,

    /// <summary>Two figures compared over different windows.</summary>
    WindowMismatch,

    /// <summary>The input breaks the engine's contract: a negative flow amount, a duplicate valuation date, a split ratio that is not positive.</summary>
    InvalidInput,

    /// <summary>A sale of more units than the recorded history holds: the history before it is missing.</summary>
    UnexplainedQuantity,
}

/// <summary>A rate (a fraction: 0.05 is 5%) or the typed reason there is none.</summary>
public sealed record MeasuredRate
{
    private MeasuredRate(decimal? value, MeasureFailure? failure)
    {
        Value = value;
        Failure = failure;
    }

    /// <summary>The rate, null exactly when <see cref="Failure"/> is set.</summary>
    public decimal? Value { get; }

    /// <summary>Why there is no rate, null exactly when <see cref="Value"/> is set.</summary>
    public MeasureFailure? Failure { get; }

    public static MeasuredRate Of(decimal value) => new(value, null);

    public static MeasuredRate Fails(MeasureFailure reason) => new(null, reason);
}

/// <summary>A composite measure result or the typed reason there is none.</summary>
public sealed record Measured<T>
    where T : class
{
    private Measured(T? value, MeasureFailure? failure)
    {
        Value = value;
        Failure = failure;
    }

    /// <summary>The result, null exactly when <see cref="Failure"/> is set.</summary>
    public T? Value { get; }

    /// <summary>Why there is no result, null exactly when <see cref="Value"/> is set.</summary>
    public MeasureFailure? Failure { get; }

    public static Measured<T> Of(T value) => new(value, null);

    public static Measured<T> Fails(MeasureFailure reason) => new(null, reason);
}
