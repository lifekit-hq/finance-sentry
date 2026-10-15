namespace FinanceSentry.Tests.Unit.Core.Measures;

using FinanceSentry.Core.Measures;

/// <summary>Terse builders for the golden fixtures. All numbers in the fixtures are fictional.</summary>
internal static class MeasureFixture
{
    /// <summary>Equality tolerance for decimal results of exact arithmetic (rounding at the 28th digit).</summary>
    public const decimal Exact = 0.000000000001m;

    public static DateOnly Day(int year, int month, int day) => new(year, month, day);

    public static ValuePoint Close(DateOnly date, decimal? valueUsd) => new(date, valueUsd);

    public static ClassifiedFlow In(DateOnly date, decimal amountUsd) => new(date, FlowClass.ExternalInflow, amountUsd);

    public static ClassifiedFlow Out(DateOnly date, decimal amountUsd) => new(date, FlowClass.ExternalOutflow, amountUsd);

    public static ClassifiedFlow Income(DateOnly date, decimal amountUsd) => new(date, FlowClass.Income, amountUsd);

    public static ClassifiedFlow Fee(DateOnly date, decimal amountUsd) => new(date, FlowClass.Fee, amountUsd);

    public static ClassifiedFlow Internal(DateOnly date, decimal amountUsd) => new(date, FlowClass.Internal, amountUsd);
}
