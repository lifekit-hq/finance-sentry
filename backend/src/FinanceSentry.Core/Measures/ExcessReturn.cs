namespace FinanceSentry.Core.Measures;

/// <summary>M9: the book's TWR, the benchmark's total return over the same window, and the difference.</summary>
public sealed record ExcessReturnResult(MeasuredRate BookReturn, MeasuredRate BenchmarkReturn, MeasuredRate Excess);

/// <summary>
/// M9, benchmark-relative return (finance-framework §3.4, GIPS 2.A.18): excess = book TWR − benchmark total
/// return over the identical window. The benchmark is a total-return index (dividends reinvested, as an
/// adjusted close), so its return is last ÷ first − 1. Pure: no I/O, no clock.
/// </summary>
public static class ExcessReturn
{
    /// <param name="book">The book's M3 result.</param>
    /// <param name="benchmarkOpening">The benchmark index at the close of the book's opening date.</param>
    /// <param name="benchmarkClosing">The benchmark index at the close of the book's closing date.</param>
    public static ExcessReturnResult Measure(TimeWeightedResult book, ValuePoint benchmarkOpening, ValuePoint benchmarkClosing)
    {
        var benchmark = BenchmarkReturn(book, benchmarkOpening, benchmarkClosing);
        var excess = (book.Return.Value, benchmark.Value) switch
        {
            (decimal b, decimal m) => MeasuredRate.Of(b - m),
            _ => MeasuredRate.Fails(book.Return.Failure ?? benchmark.Failure!.Value),
        };

        return new ExcessReturnResult(book.Return, benchmark, excess);
    }

    private static MeasuredRate BenchmarkReturn(TimeWeightedResult book, ValuePoint opening, ValuePoint closing)
    {
        if (opening.Date != book.From || closing.Date != book.To)
            return MeasuredRate.Fails(MeasureFailure.WindowMismatch);
        if (opening.ValueUsd is not decimal first || closing.ValueUsd is not decimal last)
            return MeasuredRate.Fails(MeasureFailure.MissingValue);

        return first <= 0m || last < 0m
            ? MeasuredRate.Fails(MeasureFailure.InvalidInput)
            : MeasuredRate.Of((last / first) - 1m);
    }
}
