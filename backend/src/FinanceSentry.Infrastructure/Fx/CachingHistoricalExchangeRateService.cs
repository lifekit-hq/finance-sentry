namespace FinanceSentry.Infrastructure.Fx;

using FinanceSentry.Core.Utils;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

/// <summary>
/// Resolves a past date's conversion rate by routing to whichever feed publishes that
/// currency (UAH → NBU, ECB currencies → Frankfurter) and caching the result.
///
/// Published rates for a past date are immutable, so the cache only exists to avoid
/// re-fetching — it never has to be invalidated for correctness. When no feed can answer,
/// conversion degrades to today's flat rate from <see cref="CurrencyConverter"/>: a
/// slightly wrong historical figure beats an empty chart.
/// </summary>
public interface IHistoricalExchangeRateService
{
    /// <summary>
    /// USD-per-unit rates covering the inclusive range, gap-filled: feeds skip weekends and
    /// holidays, so each missing day carries the previous published rate forward (and days
    /// before the first published rate carry the first one backward).
    /// </summary>
    Task<IReadOnlyDictionary<DateOnly, decimal>> GetDailySeriesAsync(
        string currency, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// The USD-per-unit rate a feed actually published for <paramref name="date"/> — or, when the
    /// feed skips that day (weekend, holiday), for the closest earlier day within
    /// <see cref="CachingHistoricalExchangeRateService.PublishedLookbackDays"/>. Returns null when
    /// no feed published one. Unlike <see cref="GetDailySeriesAsync"/> it never substitutes
    /// today's live rate, so a caller that must not guess can tell a real rate from a fallback.
    /// </summary>
    Task<decimal?> GetPublishedRateAsync(string currency, DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// The USD-per-unit rates a feed actually published inside the inclusive range, with the window
    /// widened backward by <see cref="CachingHistoricalExchangeRateService.PublishedLookbackDays"/> so
    /// the first day can borrow the closest earlier published rate. One lookup (and one upstream
    /// request) covers the whole range; resolve each date with <see cref="PublishedRateLookup.Resolve"/>.
    /// Days a feed skips are absent and today's live rate is never substituted. Empty when no feed
    /// published one (an unreachable or unsupported feed), and that outcome is not cached.
    /// </summary>
    Task<IReadOnlyDictionary<DateOnly, decimal>> GetPublishedSeriesAsync(
        string currency, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// Converts <paramref name="amount"/> to USD at <paramref name="date"/>'s rate and reports how the
    /// rate was obtained: <see cref="FxStatus.Exact"/> when a feed published that very date,
    /// <see cref="FxStatus.NearestPublished"/> when it borrowed the closest earlier published day, and
    /// otherwise today's rate from <see cref="CurrencyConverter"/> - at least
    /// <see cref="FxStatus.TodayRate"/>, worse when that rate is itself the seed
    /// (<see cref="FxStatus.SeedTable"/>) or absent (<see cref="FxStatus.Unknown1To1"/>). Never throws
    /// on an unknown currency.
    /// </summary>
    Task<ConvertResult> ConvertToUsdAsync(
        decimal amount, string currency, DateOnly date, CancellationToken ct = default);
}

/// <summary>Resolves one date against a published series without ever reaching for today's rate.</summary>
public static class PublishedRateLookup
{
    /// <summary>
    /// The rate published for <paramref name="date"/> (<see cref="FxStatus.Exact"/>), else the closest
    /// earlier published rate within <see cref="CachingHistoricalExchangeRateService.PublishedLookbackDays"/>
    /// (<see cref="FxStatus.NearestPublished"/>), else null.
    /// </summary>
    public static (decimal Rate, FxStatus Status)? Resolve(IReadOnlyDictionary<DateOnly, decimal> published, DateOnly date)
    {
        if (published.TryGetValue(date, out var exact))
            return (exact, FxStatus.Exact);

        for (var back = 1; back <= CachingHistoricalExchangeRateService.PublishedLookbackDays; back++)
        {
            if (published.TryGetValue(date.AddDays(-back), out var nearest))
                return (nearest, FxStatus.NearestPublished);
        }

        return null;
    }
}

public sealed class CachingHistoricalExchangeRateService : IHistoricalExchangeRateService
{
    /// <summary>How far back a non-publishing day (weekend, holiday) may borrow the last published rate.</summary>
    public const int PublishedLookbackDays = 7;

    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(12);

    private readonly IReadOnlyList<IHistoricalExchangeRateProvider> _providers;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CachingHistoricalExchangeRateService> _logger;

    public CachingHistoricalExchangeRateService(
        IEnumerable<IHistoricalExchangeRateProvider> providers,
        IMemoryCache cache,
        ILogger<CachingHistoricalExchangeRateService> logger)
    {
        _providers = providers.ToList();
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<DateOnly, decimal>> GetDailySeriesAsync(
        string currency, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(currency) || to < from)
            return new Dictionary<DateOnly, decimal>();

        var normalized = currency.Trim().ToUpperInvariant();
        var cacheKey = $"fx-history:{normalized}:{from:yyyyMMdd}:{to:yyyyMMdd}";

        if (_cache.TryGetValue(cacheKey, out IReadOnlyDictionary<DateOnly, decimal>? cached) && cached is not null)
            return cached;

        var published = await FetchPublishedAsync(normalized, from, to, ct);
        var series = FillGaps(published, normalized, from, to);

        _cache.Set(cacheKey, series, CacheLifetime);
        return series;
    }

    public async Task<decimal?> GetPublishedRateAsync(string currency, DateOnly date, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(currency))
            return null;

        var normalized = currency.Trim().ToUpperInvariant();
        var published = await FetchPublishedAsync(normalized, date.AddDays(-PublishedLookbackDays), date, ct);

        var onOrBefore = published.Where(kv => kv.Key <= date).OrderByDescending(kv => kv.Key).ToList();
        return onOrBefore.Count > 0 ? onOrBefore[0].Value : null;
    }

    public async Task<IReadOnlyDictionary<DateOnly, decimal>> GetPublishedSeriesAsync(
        string currency, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(currency) || to < from)
            return new Dictionary<DateOnly, decimal>();

        var normalized = currency.Trim().ToUpperInvariant();
        var cacheKey = $"fx-published:{normalized}:{from:yyyyMMdd}:{to:yyyyMMdd}";

        if (_cache.TryGetValue(cacheKey, out IReadOnlyDictionary<DateOnly, decimal>? cached) && cached is not null)
            return cached;

        var published = await FetchPublishedAsync(normalized, from.AddDays(-PublishedLookbackDays), to, ct);

        // An empty answer is an outage or an unsupported currency, not a fact about the dates: do not pin it.
        if (published.Count > 0)
            _cache.Set(cacheKey, published, CacheLifetime);

        return published;
    }

    public async Task<ConvertResult> ConvertToUsdAsync(
        decimal amount, string currency, DateOnly date, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(currency))
        {
            var normalized = currency.Trim().ToUpperInvariant();
            if (normalized == "USD")
                return new ConvertResult(amount, FxStatus.Exact);

            var published = await FetchPublishedAsync(normalized, date.AddDays(-PublishedLookbackDays), date, ct);

            if (PublishedRateLookup.Resolve(published, date) is { } resolved)
                return new ConvertResult(amount * resolved.Rate, resolved.Status);
        }

        var today = CurrencyConverter.Convert(amount, currency);
        return today with { Status = today.Status.Worst(FxStatus.TodayRate) };
    }

    private async Task<IReadOnlyDictionary<DateOnly, decimal>> FetchPublishedAsync(
        string currency, DateOnly from, DateOnly to, CancellationToken ct)
    {
        foreach (var provider in _providers.Where(p => p.Supports(currency)))
        {
            var series = await provider.GetUsdPerUnitSeriesAsync(currency, from, to, ct);
            if (series.Count > 0)
                return series;
        }

        _logger.LogInformation(
            "No historical feed produced {Currency} rates for {From}..{To}; using the live rate for the whole window.",
            currency, from, to);
        return new Dictionary<DateOnly, decimal>();
    }

    private static IReadOnlyDictionary<DateOnly, decimal> FillGaps(
        IReadOnlyDictionary<DateOnly, decimal> published, string currency, DateOnly from, DateOnly to)
    {
        var fallback = CurrencyConverter.ToUsd(1m, currency);
        var seed = published.Count > 0
            ? published.OrderBy(kv => kv.Key).First().Value
            : fallback;

        var filled = new Dictionary<DateOnly, decimal>();
        var carried = seed;

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (published.TryGetValue(date, out var rate))
                carried = rate;

            filled[date] = carried;
        }

        return filled;
    }
}
