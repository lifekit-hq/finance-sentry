namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
using FinanceSentry.Modules.Research.Domain;
using Microsoft.Extensions.Logging;

/// <summary>
/// Builds a trailing-P/E average from reported diluted-EPS quarters (the fundamentals chain) × Yahoo daily closes (feature 030,
/// R3). At each quarter-end we roll the trailing four quarters into a TTM EPS and price it against the
/// close on/just before that date; the average of those points is the "5-year" trailing P/E, over
/// whatever window the filings actually cover. Deterministic and source-grounded — no fabrication.
/// </summary>
public sealed class ValuationHistoryService(
    IFundamentalsService fundamentals,
    IMarketDataService marketData,
    ILogger<ValuationHistoryService> logger) : IValuationHistoryService
{
    private const string DilutedEpsConcept = "DilutedEPS";
    private const int MaxQuartersPerConcept = 20;
    private const int TtmQuarters = 4;
    private const int LookbackYears = 5;
    private const int CloseMatchToleranceDays = 10;
    private const int DaysPerYear = 365;

    public async Task<TrailingPeHistory> GetTrailingPeHistoryAsync(string ticker, CancellationToken ct = default)
    {
        var upper = ticker.Trim().ToUpperInvariant();

        var facts = (await fundamentals.GetFundamentalsAsync(upper, MaxQuartersPerConcept, ct)).Facts;
        var quarters = ExtractQuarterlyEps(facts);
        if (quarters.Count < TtmQuarters)
        {
            return new TrailingPeHistory(null, null);
        }

        var since = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-LookbackYears));
        var closes = await marketData.GetDailyClosesAsync(upper, since, ct);
        if (closes.Count == 0)
        {
            return new TrailingPeHistory(null, null);
        }

        var closesDesc = closes.OrderByDescending(c => c.Date).ToList();

        var samples = new List<decimal>();
        DateOnly? earliest = null;
        DateOnly? latest = null;

        for (var i = TtmQuarters - 1; i < quarters.Count; i++)
        {
            var ttm = 0m;
            for (var q = i - (TtmQuarters - 1); q <= i; q++)
            {
                ttm += quarters[q].Eps;
            }

            if (ttm <= 0m)
            {
                continue;
            }

            var asOf = quarters[i].PeriodEnd;
            var close = FindCloseOnOrBefore(closesDesc, asOf);
            if (close is null)
            {
                continue;
            }

            samples.Add(close.Value / ttm);
            earliest ??= asOf;
            latest = asOf;
        }

        if (samples.Count == 0 || earliest is null || latest is null)
        {
            return new TrailingPeHistory(null, null);
        }

        var avg = samples.Average();
        var spanDays = latest.Value.DayNumber - earliest.Value.DayNumber;
        var windowYears = Math.Max(1, (int)Math.Round((double)spanDays / DaysPerYear, MidpointRounding.AwayFromZero));

        logger.LogDebug("Trailing-P/E history for {Ticker}: {Samples} points over {Years}y avg {Avg}",
            upper, samples.Count, windowYears, avg);

        return new TrailingPeHistory(decimal.Round(avg, 2), windowYears);
    }

    // One diluted-EPS value per fiscal quarter-end, oldest first. XBRL reports both quarterly and
    // year-to-date frames; we keep the quarterly points (Q1–Q4) and dedupe by period end.
    private static IReadOnlyList<(DateOnly PeriodEnd, decimal Eps)> ExtractQuarterlyEps(
        IReadOnlyList<FundamentalFact> facts)
    {
        return facts
            .Where(f => f.Concept == DilutedEpsConcept && IsQuarter(f.FiscalPeriod))
            .GroupBy(f => f.PeriodEnd)
            .Select(g => (PeriodEnd: g.Key, Eps: g.First().Value))
            .OrderBy(x => x.PeriodEnd)
            .ToList();
    }

    private static bool IsQuarter(string? fiscalPeriod) =>
        fiscalPeriod is "Q1" or "Q2" or "Q3" or "Q4";

    private static decimal? FindCloseOnOrBefore(IReadOnlyList<DailyClose> closesDesc, DateOnly asOf)
    {
        foreach (var close in closesDesc)
        {
            if (close.Date <= asOf)
            {
                return asOf.DayNumber - close.Date.DayNumber <= CloseMatchToleranceDays ? close.Close : null;
            }
        }

        return null;
    }
}
