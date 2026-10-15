namespace FinanceSentry.Tests.Unit.Fx;

using FinanceSentry.Core.Utils;
using FinanceSentry.Infrastructure.Fx;

/// <summary>
/// A feed that published exactly the rates it is given (USD per unit, by currency and date) and
/// counts how often a series was asked for, so a test can assert one batched lookup.
/// </summary>
public sealed class StubPublishedRates(Dictionary<string, Dictionary<DateOnly, decimal>>? published = null)
    : IHistoricalExchangeRateService
{
    public int SeriesCalls { get; private set; }

    public DateOnly? LastFrom { get; private set; }

    public DateOnly? LastTo { get; private set; }

    public Task<IReadOnlyDictionary<DateOnly, decimal>> GetPublishedSeriesAsync(
        string currency, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        SeriesCalls++;
        LastFrom = from;
        LastTo = to;
        IReadOnlyDictionary<DateOnly, decimal> series = published is not null && published.TryGetValue(currency, out var rates)
            ? rates.Where(kv => kv.Key >= from.AddDays(-CachingHistoricalExchangeRateService.PublishedLookbackDays) && kv.Key <= to)
                .ToDictionary(kv => kv.Key, kv => kv.Value)
            : new Dictionary<DateOnly, decimal>();
        return Task.FromResult(series);
    }

    public Task<IReadOnlyDictionary<DateOnly, decimal>> GetDailySeriesAsync(
        string currency, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<decimal?> GetPublishedRateAsync(string currency, DateOnly date, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<ConvertResult> ConvertToUsdAsync(
        decimal amount, string currency, DateOnly date, CancellationToken ct = default) =>
        throw new NotSupportedException();
}
