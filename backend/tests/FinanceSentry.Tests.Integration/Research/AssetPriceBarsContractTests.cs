namespace FinanceSentry.Tests.Integration.Research;

using System.Net;
using System.Net.Http.Json;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FluentAssertions;
using Moq;
using Xunit;

// GET /research/assets/{symbol}/bars: validation, the period's window and warm-up, the 52-week range and the
// crypto ticker mapping. The bar source is stubbed through the shared AssetDossierApiFactory; each test uses its
// own symbol so the shared mocks never leak between tests.

public class AssetPriceBarsContractTests(AssetDossierApiFactory factory)
    : IClassFixture<AssetDossierApiFactory>
{
    private static readonly long LastDay = new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
    private const long Day = 86_400;

    private readonly HttpClient _client = factory.CreateAuthenticatedClient();
    private readonly AssetDossierApiFactory _factory = factory;

    [Fact]
    public async Task GetBars_NoAuth_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/research/assets/AAPL/bars");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetBars_UnknownPeriod_Returns400WithErrorCode()
    {
        var response = await _client.GetAsync("/api/v1/research/assets/AAPL/bars?period=2W");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ErrorShape>())!.ErrorCode.Should().Be("INVALID_PRICE_BAR_REQUEST");
    }

    [Fact]
    public async Task GetBars_IntervalTheSourceCannotServeForThePeriod_Returns400()
    {
        // Five-minute bars exist only for the recent past; a five-year chart cannot be drawn from them.
        var response = await _client.GetAsync("/api/v1/research/assets/AAPL/bars?period=5Y&interval=5m");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ErrorShape>())!.ErrorCode.Should().Be("INVALID_PRICE_BAR_REQUEST");
    }

    [Fact]
    public async Task GetBars_NoHistory_Returns200WithNoBars_AndTheDefaultPeriod()
    {
        var response = await _client.GetAsync("/api/v1/research/assets/NOBARS/bars");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BarsShape>();
        body!.Period.Should().Be("1Y");
        body.Interval.Should().Be("1d");
        body.Intervals.Should().Equal("1d", "1w");
        body.Bars.Should().BeEmpty();
        body.WeekRange.Should().BeNull();
    }

    [Fact]
    public async Task GetBars_ThreeMonths_ReturnsTheWindowWithWarmupAndTheWeekRange()
    {
        const string symbol = "BARS";
        var daily = Series(symbol, 400);
        StubBars(symbol, "1d", daily);

        var response = await _client.GetAsync($"/api/v1/research/assets/{symbol.ToLowerInvariant()}/bars?period=3m");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BarsShape>();
        body!.Symbol.Should().Be(symbol);
        body.Period.Should().Be("3M");
        body.Interval.Should().Be("1d");
        body.Currency.Should().Be("USD");
        body.Truncated.Should().BeFalse();
        body.Bars.Should().HaveCountGreaterThan(200, "the 200 bars before the window feed the moving averages");
        body.Bars.Count(b => b.Time >= body.WindowStart).Should().BeInRange(89, 93);
        body.Bars[^1].Time.Should().Be(LastDay);
        body.WeekRange.Should().NotBeNull();
        body.WeekRange!.CoversYear.Should().BeTrue();
        body.WeekRange.High.Should().Be(daily.Bars.Where(b => b.Time > LastDay - (364 * Day)).Max(b => b.High));
    }

    [Fact]
    public async Task GetBars_HeldCrypto_AsksForTheUsdPair()
    {
        const string symbol = "BTCX";
        var position = new BookFigurePosition(symbol, "crypto", 1m, 100m, 150m, "binance");
        _factory.BookFiguresMock
            .Setup(s => s.ReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BookFigures(0m, 0m, 0m, 150m, 150m, [position], false, []));
        StubBars($"{symbol}-USD", "1d", Series(symbol, 30));

        var response = await _client.GetAsync($"/api/v1/research/assets/{symbol}/bars?period=1M&interval=1d");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<BarsShape>())!.Bars.Should().NotBeEmpty();
    }

    private static PriceBarSeries Series(string seed, int count)
    {
        _ = seed;
        var bars = Enumerable.Range(0, count)
            .Select(i => new PriceBar(LastDay - ((count - 1 - i) * Day), 100m + i, 101m + i, 99m + i, 100.5m + i, 1_000 + i))
            .ToList();
        return new PriceBarSeries("USD", "America/New_York", bars);
    }

    private void StubBars(string ticker, string interval, PriceBarSeries series)
        => _factory.PriceBarsMock
            .Setup(s => s.GetAsync(ticker, interval, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(series);

    private sealed record ErrorShape(string Error, string ErrorCode);

    private sealed record BarShape(long Time, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

    private sealed record WeekShape(decimal High, decimal Low, DateOnly From, bool CoversYear);

    private sealed record BarsShape(
        string Symbol,
        string Period,
        string Interval,
        List<string> Intervals,
        string? Currency,
        string? TimeZone,
        List<BarShape> Bars,
        long WindowStart,
        bool Truncated,
        WeekShape? WeekRange,
        string Source);
}
