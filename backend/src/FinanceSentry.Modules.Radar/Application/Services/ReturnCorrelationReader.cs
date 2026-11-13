namespace FinanceSentry.Modules.Radar.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Domain.MarketStructure;
using FinanceSentry.Modules.Radar.Domain.Repositories;

/// <summary>
/// <see cref="IReturnCorrelationReader"/> over the daily bars Radar already ingests. Bars are read per
/// ticker (the book's positions plus the subject), converted to adjusted-close returns and handed to
/// <see cref="ReturnCorrelation"/>; nothing is fetched from a provider.
/// </summary>
public sealed class ReturnCorrelationReader(IDailyBarRepository bars) : IReturnCorrelationReader
{
    // Calendar lookback that comfortably covers WindowReturns + 1 trading closes across holidays.
    private const int LookbackCalendarDays = ReturnCorrelation.WindowReturns * 2;

    public async Task<TickerCorrelation> GetAsync(
        string ticker,
        IReadOnlyCollection<CorrelationReference> book,
        int topHoldings,
        decimal minBookCoverage,
        CancellationToken ct = default)
    {
        var subject = Normalize(ticker);
        var positions = book
            .Where(p => p.Weight > 0 && !string.Equals(Normalize(p.Symbol), subject, StringComparison.Ordinal))
            .GroupBy(p => Normalize(p.Symbol))
            .Select(g => (Symbol: g.Key, Weight: g.Sum(p => p.Weight)))
            .OrderByDescending(p => p.Weight)
            .ThenBy(p => p.Symbol, StringComparer.Ordinal)
            .ToList();
        var total = positions.Sum(p => p.Weight);

        var subjectReturns = await LoadReturnsAsync(subject, ct);
        var returnsBySymbol = new Dictionary<string, IReadOnlyDictionary<DateOnly, decimal>>(StringComparer.Ordinal);
        foreach (var position in positions)
        {
            returnsBySymbol[position.Symbol] = await LoadReturnsAsync(position.Symbol, ct);
        }

        var top = positions.Take(Math.Max(topHoldings, 0))
            .Select(p => ToReference(p.Symbol, p.Weight / total, subjectReturns, returnsBySymbol[p.Symbol]))
            .ToList();

        return new TickerCorrelation(
            subject, ReturnCorrelation.WindowReturns, top,
            ComputeBook(positions, total, subjectReturns, returnsBySymbol, minBookCoverage));
    }

    private static ReferenceCorrelation ToReference(
        string symbol,
        decimal weight,
        IReadOnlyDictionary<DateOnly, decimal> subjectReturns,
        IReadOnlyDictionary<DateOnly, decimal> holdingReturns)
    {
        var outcome = subjectReturns.Count == 0 || holdingReturns.Count == 0
            ? new ReturnCorrelation.Outcome(null, 0, CorrelationReasons.NoBars)
            : ReturnCorrelation.Pearson(subjectReturns, holdingReturns);
        return new ReferenceCorrelation(symbol, Math.Round(weight, 4), outcome.Value, outcome.Observations, outcome.Reason);
    }

    private static BookCorrelation ComputeBook(
        List<(string Symbol, decimal Weight)> positions,
        decimal total,
        IReadOnlyDictionary<DateOnly, decimal> subjectReturns,
        Dictionary<string, IReadOnlyDictionary<DateOnly, decimal>> returnsBySymbol,
        decimal minBookCoverage)
    {
        if (total <= 0)
        {
            return new BookCorrelation(null, 0, 0m, CorrelationReasons.EmptyBook);
        }

        // A position counts toward the book only with enough of its own history to be a real series.
        var usable = positions
            .Where(p => returnsBySymbol[p.Symbol].Count >= ReturnCorrelation.MinObservations)
            .ToList();
        var coverage = Math.Round(usable.Sum(p => p.Weight) / total, 4);
        if (subjectReturns.Count == 0)
        {
            return new BookCorrelation(null, 0, coverage, CorrelationReasons.NoBars);
        }

        if (usable.Count == 0 || coverage < minBookCoverage)
        {
            return new BookCorrelation(null, 0, coverage, CorrelationReasons.BookCoverageTooLow);
        }

        var bookReturns = ReturnCorrelation.WeightedBookReturns(
            usable.Select(p => (returnsBySymbol[p.Symbol], p.Weight)).ToList());
        var outcome = ReturnCorrelation.Pearson(subjectReturns, bookReturns);
        return new BookCorrelation(outcome.Value, outcome.Observations, coverage, outcome.Reason);
    }

    private async Task<IReadOnlyDictionary<DateOnly, decimal>> LoadReturnsAsync(string ticker, CancellationToken ct)
    {
        var since = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-LookbackCalendarDays);
        var series = await bars.GetSinceAsync(ticker, since, ct);
        // Last write wins on a duplicate date; the repository is unique on (ticker, date) so this is defensive.
        var closes = new Dictionary<DateOnly, decimal>(series.Count);
        foreach (var bar in series)
        {
            closes[bar.Date] = bar.AdjClose;
        }

        return ReturnCorrelation.DailyReturns(closes);
    }

    private static string Normalize(string symbol) => symbol.Trim().ToUpperInvariant();
}
