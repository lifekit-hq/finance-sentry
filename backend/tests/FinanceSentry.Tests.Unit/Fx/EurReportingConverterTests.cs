namespace FinanceSentry.Tests.Unit.Fx;

using FinanceSentry.Infrastructure.Fx;
using FluentAssertions;
using Xunit;

public class EurReportingConverterTests
{
    private sealed class StubRates(Dictionary<(string, DateOnly), decimal> published) : IHistoricalExchangeRateService
    {
        public Task<IReadOnlyDictionary<DateOnly, decimal>> GetDailySeriesAsync(
            string currency, DateOnly from, DateOnly to, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<decimal?> GetPublishedRateAsync(string currency, DateOnly date, CancellationToken ct = default) =>
            Task.FromResult<decimal?>(published.TryGetValue((currency, date), out var r) ? r : null);
    }

    private static readonly DateOnly Day = new(2024, 3, 15);

    [Fact]
    public async Task Eur_PassesThroughUnchanged_WithoutARate()
    {
        var sut = new EurReportingConverter(new StubRates([]));

        var result = await sut.ToEurAsync(123.45m, "eur", Day);

        result.Should().Be(EurConversion.Of(123.45m));
    }

    [Fact]
    public async Task ForeignAmount_UsesTheRateForThatDate_NotTheLiveRate()
    {
        // Live table has GBP at 1.27 / EUR at 1.08; the dated feed says otherwise.
        var sut = new EurReportingConverter(new StubRates(new()
        {
            [("GBP", Day)] = 1.30m,
            [("EUR", Day)] = 1.10m,
        }));

        var result = await sut.ToEurAsync(100m, "GBP", Day);

        result.RateMissing.Should().BeFalse();
        result.AmountEur.Should().BeApproximately(100m * 1.30m / 1.10m, 0.0001m);
    }

    [Fact]
    public async Task Usd_ConvertsViaTheEurRateOfThatDate()
    {
        var sut = new EurReportingConverter(new StubRates(new() { [("EUR", Day)] = 1.25m }));

        var result = await sut.ToEurAsync(100m, "USD", Day);

        result.AmountEur.Should().BeApproximately(80m, 0.0001m);
    }

    [Fact]
    public async Task MissingRate_IsSurfaced_NotZeroOrLiveRate()
    {
        var sut = new EurReportingConverter(new StubRates(new() { [("EUR", Day)] = 1.10m }));

        var result = await sut.ToEurAsync(100m, "UAH", Day);

        result.RateMissing.Should().BeTrue();
        result.AmountEur.Should().BeNull();
    }
}
