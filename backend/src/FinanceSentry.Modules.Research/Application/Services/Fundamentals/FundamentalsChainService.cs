namespace FinanceSentry.Modules.Research.Application.Services.Fundamentals;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Fundamentals;
using FinanceSentry.Modules.Research.Domain.Scoring;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// The ordered fundamentals provider chain (#837). Sources are asked in <c>Fundamentals:SourceOrder</c>
/// order, and each period basis (quarterly, annual) is supplied whole by ONE source: the first whose
/// series on that basis is fresh. A series is therefore never stitched from two providers, so a margin
/// never divides one provider's operating income by another's revenue. When no source has a fresh
/// series, the freshest stale one is used and flagged. A source that fails (an outage, not "no data")
/// stops the fall-through: a later provider's numbers never stand in for an unreachable earlier one,
/// which would make a deterministic monitor read a different series on a bad night.
/// A ticker the first source covers fully never reaches the others.
/// </summary>
public sealed class FundamentalsChainService(
    IEnumerable<IFundamentalsSource> sources,
    IOptions<FundamentalsOptions> options,
    TimeProvider clock,
    ILogger<FundamentalsChainService> logger) : IFundamentalsService
{
    private const int MinPerConcept = 1;
    private const int MaxPerConcept = 20;

    private static readonly string[] Bases = [FundamentalsBasis.Quarterly, FundamentalsBasis.Annual];

    private readonly FundamentalsOptions settings = options.Value;
    private readonly IReadOnlyList<IFundamentalsSource> orderedSources = Order(sources, options.Value, logger);

    public async Task<FundamentalsResult> GetFundamentalsAsync(
        string ticker, int maxPerConcept, CancellationToken ct = default)
    {
        var upper = ticker.Trim().ToUpperInvariant();
        var perConcept = Math.Clamp(maxPerConcept, MinPerConcept, MaxPerConcept);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        var chosen = new Dictionary<string, Series>(StringComparer.Ordinal);
        var staleFallback = new Dictionary<string, Series>(StringComparer.Ordinal);
        var noData = new List<string>();
        var answers = new Dictionary<string, IReadOnlyList<FundamentalFact>>(StringComparer.Ordinal);
        string? issuerType = null;
        string? failure = null;

        foreach (var source in orderedSources)
        {
            if (chosen.Count == Bases.Length)
            {
                break;
            }

            var result = await FetchAsync(source, upper, ct);
            issuerType ??= result.IssuerType;

            if (result.Outcome == FundamentalsSourceOutcome.Failed)
            {
                failure = $"{source.Name} unavailable: {result.Detail}";
                break;
            }

            if (result.Outcome == FundamentalsSourceOutcome.NoData)
            {
                noData.Add($"{source.Name}: {result.Detail}");
                continue;
            }

            answers[source.Name] = result.Facts;
            foreach (var basis in Bases.Where(b => !chosen.ContainsKey(b)))
            {
                var periodEnds = result.Facts.Where(f => BasisOf(f) == basis).Select(f => f.PeriodEnd).ToList();
                if (periodEnds.Count == 0)
                {
                    continue;
                }

                var series = new Series(source.Name, periodEnds.Max(), Stale: false);
                if (IsFresh(series, basis, today))
                {
                    chosen[basis] = series;
                }
                else if (!staleFallback.TryGetValue(basis, out var held) || series.Latest > held.Latest)
                {
                    staleFallback[basis] = series with { Stale = true };
                }
            }
        }

        foreach (var (basis, series) in staleFallback)
        {
            chosen.TryAdd(basis, series);
        }

        // Keep each source's own fact order (only filtered to the bases it supplies), so a single-source
        // answer is exactly that source's answer, ties included.
        var all = chosen.Values
            .Select(s => s.Provider)
            .Distinct()
            .SelectMany(provider => answers[provider].Where(f =>
                chosen.TryGetValue(BasisOf(f), out var series) && series.Provider == provider))
            .ToList();
        var coverage = BuildCoverage(upper, chosen, issuerType, failure, noData);

        logger.LogDebug(
            "Fundamentals for {Ticker}: {Status}; {Bases}",
            upper,
            coverage.Status,
            string.Join(", ", coverage.Bases.Select(b => $"{b.Basis}={b.Provider}{(b.Stale ? " (stale)" : string.Empty)}")));

        return new FundamentalsResult(upper, Trim(all, perConcept), coverage);
    }

    // A fiscal-year fact is annual; every other fact is quarterly (EDGAR's Q1-Q4, a provider's 3M).
    private static string BasisOf(FundamentalFact fact)
        => string.Equals(fact.FiscalPeriod, FundamentalMath.FiscalYearPeriod, StringComparison.OrdinalIgnoreCase)
            ? FundamentalsBasis.Annual
            : FundamentalsBasis.Quarterly;

    private bool IsFresh(Series series, string basis, DateOnly today)
    {
        var windowDays = basis == FundamentalsBasis.Annual
            ? settings.AnnualFreshnessDays
            : settings.QuarterlyFreshnessDays;
        return today.DayNumber - series.Latest.DayNumber <= windowDays;
    }

    private static FundamentalsCoverage BuildCoverage(
        string ticker,
        IReadOnlyDictionary<string, Series> chosen,
        string? issuerType,
        string? failure,
        IReadOnlyList<string> noData)
    {
        var bases = Bases
            .Where(chosen.ContainsKey)
            .Select(b => new FundamentalsBasisCoverage(b, chosen[b].Provider, chosen[b].Latest, chosen[b].Stale))
            .ToList();

        if (bases.Count > 0)
        {
            var missing = Bases.Where(b => !chosen.ContainsKey(b)).Select(b => $"no source has {b} periods");
            var notes = missing.Concat(failure is null ? [] : [failure]).ToList();
            return new FundamentalsCoverage(
                FundamentalsCoverageStatus.Covered, issuerType, notes.Count == 0 ? null : string.Join("; ", notes), bases);
        }

        if (issuerType == FundamentalsIssuerType.InvestmentFund)
        {
            return new FundamentalsCoverage(
                FundamentalsCoverageStatus.UnsupportedIssuerType,
                issuerType,
                $"{ticker} is an investment fund: it files fund reports, not company financial statements",
                bases);
        }

        if (failure is not null)
        {
            return new FundamentalsCoverage(FundamentalsCoverageStatus.SourceUnavailable, issuerType, failure, bases);
        }

        var tried = noData.Count == 0 ? "no source is configured" : string.Join("; ", noData);
        return new FundamentalsCoverage(FundamentalsCoverageStatus.NoSourceAvailable, issuerType, tried, bases);
    }

    private async Task<FundamentalsSourceResult> FetchAsync(IFundamentalsSource source, string ticker, CancellationToken ct)
    {
        try
        {
            return await source.FetchAsync(ticker, ct);
        }
        // An HttpClient timeout is a TaskCanceledException too: only the caller's own cancellation propagates.
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Fundamentals source {Source} threw for {Ticker}", source.Name, ticker);
            return FundamentalsSourceResult.Failed(ex.Message);
        }
    }

    private static IReadOnlyList<FundamentalFact> Trim(IReadOnlyList<FundamentalFact> facts, int perConcept)
        => facts
            .GroupBy(f => f.Concept)
            .SelectMany(g => g.OrderByDescending(f => f.PeriodEnd).Take(perConcept))
            .OrderBy(f => f.Concept, StringComparer.Ordinal)
            .ThenByDescending(f => f.PeriodEnd)
            .ToList();

    private static IReadOnlyList<IFundamentalsSource> Order(
        IEnumerable<IFundamentalsSource> sources, FundamentalsOptions settings, ILogger logger)
    {
        var byName = new Dictionary<string, IFundamentalsSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            byName.TryAdd(source.Name, source);
        }

        IReadOnlyList<string> order = settings.SourceOrder is { Length: > 0 } configured
            ? configured
            : FundamentalsOptions.DefaultSourceOrder;

        var ordered = new List<IFundamentalsSource>();
        foreach (var name in order)
        {
            if (!byName.TryGetValue(name.Trim(), out var source))
            {
                logger.LogWarning("Fundamentals:SourceOrder names unknown source {Source}; skipped", name);
                continue;
            }

            if (!ordered.Contains(source))
            {
                ordered.Add(source);
            }
        }

        return ordered;
    }

    private sealed record Series(string Provider, DateOnly Latest, bool Stale);
}
