namespace FinanceSentry.Core.Headlines;

using FinanceSentry.Core.Measures;
using FinanceSentry.Core.Utils;

/// <summary>A position as a gain-vs-cost headline reads it: its value and its cost basis (null or 0 when unknown).</summary>
public readonly record struct CostedValue(decimal ValueUsd, decimal? CostBasisUsd);

/// <summary>
/// Builds <see cref="Headline"/> records in USD (finance-framework C4: compute in USD, show in the base currency).
/// Each builder describes a figure the caller already computes; percents come from the measure engine
/// (<see cref="ValueChange"/>, <see cref="MeasuredRate"/>), so a missing percent always has a typed reason.
/// </summary>
public static class HeadlineFactory
{
    private const string Usd = "USD";
    private const decimal PercentScale = 100m;
    private const int PercentDecimals = 2;
    private const int CoverageDecimals = 4;

    /// <summary>A point-in-time value: net worth, a sleeve's total, the cash or invested line under it.</summary>
    public static Headline Value(
        decimal? amountUsd, string scope, DateOnly date, HeadlineSignals signals, DateTimeOffset? asOf = null)
    {
        var amount = signals.NoSplit ? null : amountUsd;
        var assessment = HeadlineQualityMapper.Assess(signals, amount is not null);
        return new Headline(
            amount, null, HeadlineMethod.Value, null, scope, new HeadlineWindow(null, date),
            assessment.Quality, null, null, assessment.Reasons, asOf, Usd);
    }

    /// <summary>
    /// M1 over a window: end minus start, and that change over the start value. A missing end of the window makes
    /// the figure unavailable (<see cref="HeadlineReasonCode.ShortHistory"/>, or <see cref="HeadlineReasonCode.NoSplit"/>
    /// alone when <paramref name="signals"/> say the split was not measured); a zero, negative or re-scoped start
    /// keeps the amount and withholds the percent.
    /// </summary>
    public static Headline Change(
        decimal? startUsd, decimal? endUsd, string scope, DateOnly from, DateOnly to,
        bool scopeChanged, HeadlineSignals signals, DateTimeOffset? asOf = null)
    {
        decimal? amount = null;
        decimal? percent = null;
        MeasureFailure? failure = MeasureFailure.MissingValue;
        if (signals.NoSplit)
        {
            failure = null;
        }
        else if (startUsd is { } start && endUsd is { } end)
        {
            var measured = ValueChange.Measure(start, end, scopeChanged);
            amount = measured.ChangeUsd;
            percent = ToPercent(measured.Percent.Value);
            failure = measured.Percent.Failure;
        }

        var assessment = HeadlineQualityMapper.Assess(signals, amount is not null, failure);
        return new Headline(
            amount, percent, HeadlineMethod.ValueChange, HeadlineBasis.StartValue, scope, new HeadlineWindow(from, to),
            assessment.Quality, startUsd is null ? BasisStatus.Missing : BasisStatus.Complete, null,
            assessment.Reasons, asOf, Usd);
    }

    /// <summary>
    /// M6 across positions, all-time to <paramref name="date"/>: value minus cost over the positions whose cost
    /// basis is known, and that gain over their cost. <see cref="Headline.Coverage"/> is the share of the scope's
    /// value those positions hold and <see cref="Headline.BasisStatus"/> says whether any are left out, so the
    /// unmeasured part is named. With no known basis the figure is unavailable, never 0%.
    /// </summary>
    public static Headline GainVsCost(
        IReadOnlyCollection<CostedValue> positions, string scope, DateOnly date, HeadlineSignals signals,
        DateTimeOffset? asOf = null)
    {
        var measured = positions.Where(p => p.CostBasisUsd is > 0m).ToList();
        var totalValue = positions.Sum(p => p.ValueUsd);
        var measuredValue = measured.Sum(p => p.ValueUsd);
        var cost = measured.Sum(p => p.CostBasisUsd!.Value);

        decimal? amount = measured.Count > 0 ? measuredValue - cost : null;
        var rate = cost > 0m ? MeasuredRate.Of((measuredValue - cost) / cost) : MeasuredRate.Fails(MeasureFailure.MissingValue);
        decimal? coverage = totalValue > 0m ? Math.Round(measuredValue / totalValue, CoverageDecimals) : null;

        var assessment = HeadlineQualityMapper.Assess(
            amount is null ? signals with { NoCostBasis = true } : signals, amount is not null);
        return new Headline(
            amount, amount is null ? null : ToPercent(rate.Value), HeadlineMethod.GainVsCost, HeadlineBasis.Cost, scope,
            new HeadlineWindow(null, date), assessment.Quality,
            HeadlineQualityMapper.BasisStatusFromCounts(measured.Count, positions.Count), coverage,
            assessment.Reasons, asOf, Usd);
    }

    /// <summary>
    /// <paramref name="headline"/> shown in <paramref name="baseCurrency"/> at today's rate. A window reaching
    /// before <paramref name="today"/> is converted at a rate that is not its own day's, so it gains
    /// <see cref="HeadlineReasonCode.FxTodayRate"/> (dated-rate conversion is not built yet); a base with no live
    /// rate gains its own FX reason. The percent is a ratio and does not change.
    /// </summary>
    public static Headline InBaseCurrency(Headline headline, string baseCurrency, DateOnly today)
    {
        if (string.Equals(baseCurrency, headline.Currency, StringComparison.OrdinalIgnoreCase))
            return headline;

        var status = CurrencyConverter.ConvertFromUsd(1m, baseCurrency).Status;
        if ((headline.Window.From ?? headline.Window.To) < today)
            status = status.Worst(FxStatus.TodayRate);

        var converted = headline with
        {
            Amount = headline.Amount is { } usd ? CurrencyConverter.FromUsd(usd, baseCurrency) : null,
            Currency = baseCurrency,
        };
        return HeadlineQualityMapper.WithFx(converted, status);
    }

    private static decimal? ToPercent(decimal? rate) =>
        rate is { } r ? Math.Round(r * PercentScale, PercentDecimals) : null;
}
