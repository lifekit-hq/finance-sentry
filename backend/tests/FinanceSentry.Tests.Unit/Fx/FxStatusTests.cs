namespace FinanceSentry.Tests.Unit.Fx;

using System.Net;
using System.Text;
using FinanceSentry.Core.Utils;
using FinanceSentry.Infrastructure.Fx;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>Framework R4: every conversion says how its rate was obtained.</summary>
public sealed class FxStatusTests
{
    private static readonly Dictionary<string, decimal> LiveEurUah = new() { ["EUR"] = 1.09m, ["UAH"] = 0.025m };

    // ---- CurrencyConverter.Convert ----

    [Fact]
    public void Convert_IsExact_ForACurrencyInTheLastLiveRefresh()
    {
        using var _ = FxTableScope.Live(LiveEurUah);

        var result = CurrencyConverter.Convert(10m, "EUR");

        result.Amount.Should().Be(10.9m);
        result.Status.Should().Be(FxStatus.Exact);
    }

    [Fact]
    public void Convert_IsSeedTable_ForACurrencyOnlyTheSeedKnows()
    {
        using var _ = FxTableScope.Seed();

        var result = CurrencyConverter.Convert(100m, "UAH");

        result.Amount.Should().Be(2.4m);
        result.Status.Should().Be(FxStatus.SeedTable);
    }

    [Fact]
    public void Convert_IsSeedTable_ForACurrencyMissingFromTheLiveFeed_EvenAfterARefresh()
    {
        using var _ = FxTableScope.Live(new Dictionary<string, decimal> { ["EUR"] = 1.09m });

        CurrencyConverter.Convert(10m, "EUR").Status.Should().Be(FxStatus.Exact);
        CurrencyConverter.Convert(100m, "UAH").Status.Should().Be(FxStatus.SeedTable);
    }

    [Theory]
    [InlineData("XYZ")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Convert_IsUnknown1To1_AndPassesTheAmountThrough_WithoutThrowing(string? currency)
    {
        using var _ = FxTableScope.Live(LiveEurUah);

        var result = CurrencyConverter.Convert(42m, currency);

        result.Amount.Should().Be(42m);
        result.Status.Should().Be(FxStatus.Unknown1To1);
    }

    [Fact]
    public void Convert_TreatsUsdAsExact_EvenOnTheSeedTable()
    {
        using var _ = FxTableScope.Seed();

        var result = CurrencyConverter.Convert(5m, "USD");

        result.Amount.Should().Be(5m);
        result.Status.Should().Be(FxStatus.Exact);
    }

    [Fact]
    public void Convert_IsCaseInsensitiveOnCurrency()
    {
        using var _ = FxTableScope.Live(LiveEurUah);

        var result = CurrencyConverter.Convert(100m, "uah");

        result.Amount.Should().Be(2.5m);
        result.Status.Should().Be(FxStatus.Exact);
    }

    [Fact]
    public void ToUsd_StaysTheAmountOnlyAdapter_OverConvert()
    {
        using var _ = FxTableScope.Seed();

        CurrencyConverter.ToUsd(100m, "UAH").Should().Be(CurrencyConverter.Convert(100m, "UAH").Amount);
        CurrencyConverter.ToUsd(42m, "XYZ").Should().Be(42m);
    }

    // ---- CurrencyConverter.ConvertFromUsd (the base-currency display leg) ----

    [Fact]
    public void ConvertFromUsd_ReportsTheStatusOfTheDisplayCurrency()
    {
        using var _ = FxTableScope.Live(new Dictionary<string, decimal> { ["EUR"] = 0.5m });

        CurrencyConverter.ConvertFromUsd(10m, "EUR").Should().Be(new ConvertResult(20m, FxStatus.Exact));
        CurrencyConverter.ConvertFromUsd(240m, "UAH").Status.Should().Be(FxStatus.SeedTable);
        CurrencyConverter.ConvertFromUsd(10m, "XYZ").Should().Be(new ConvertResult(10m, FxStatus.Unknown1To1));
    }

    // ---- FxStatus ordering and reason mapping ----

    [Fact]
    public void Worst_PicksTheWorseStatus()
    {
        FxStatus.Exact.Worst(FxStatus.NearestPublished).Should().Be(FxStatus.NearestPublished);
        FxStatus.SeedTable.Worst(FxStatus.TodayRate).Should().Be(FxStatus.SeedTable);
        FxStatus.Unknown1To1.Worst(FxStatus.Exact).Should().Be(FxStatus.Unknown1To1);
    }

    [Theory]
    [InlineData(FxStatus.Exact, null)]
    [InlineData(FxStatus.NearestPublished, null)]
    [InlineData(FxStatus.TodayRate, "FX_TODAY_RATE")]
    [InlineData(FxStatus.SeedTable, "FX_TODAY_RATE")]
    [InlineData(FxStatus.Unknown1To1, "FX_UNKNOWN_1TO1")]
    public void ToReason_MapsEachStatusToItsReasonCode(FxStatus status, string? expected)
    {
        status.ToReason().Should().Be(expected);
        status.ToReasons().Should().BeEquivalentTo(expected is null ? [] : new[] { expected });
    }

    // ---- CachingHistoricalExchangeRateService.ConvertToUsdAsync ----

    private sealed class FixedProvider(string currency, Dictionary<DateOnly, decimal> series) : IHistoricalExchangeRateProvider
    {
        public bool Supports(string c) => string.Equals(c, currency, StringComparison.OrdinalIgnoreCase);

        public Task<IReadOnlyDictionary<DateOnly, decimal>> GetUsdPerUnitSeriesAsync(
            string c, DateOnly from, DateOnly to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<DateOnly, decimal>>(
                series.Where(kv => kv.Key >= from && kv.Key <= to).ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    private static CachingHistoricalExchangeRateService Service(params IHistoricalExchangeRateProvider[] providers) =>
        new(providers, new MemoryCache(new MemoryCacheOptions()), NullLogger<CachingHistoricalExchangeRateService>.Instance);

    private static readonly DateOnly Friday = new(2024, 1, 5);
    private static readonly DateOnly Sunday = new(2024, 1, 7);

    [Fact]
    public async Task ConvertToUsdAsync_IsExact_WhenAFeedPublishedTheDate()
    {
        using var _ = FxTableScope.Live(LiveEurUah);
        var sut = Service(new FixedProvider("UAH", new() { [Friday] = 0.0263m }));

        var result = await sut.ConvertToUsdAsync(100m, "UAH", Friday);

        result.Amount.Should().Be(2.63m);
        result.Status.Should().Be(FxStatus.Exact);
    }

    [Fact]
    public async Task ConvertToUsdAsync_IsNearestPublished_ForANonPublishingDay()
    {
        using var _ = FxTableScope.Live(LiveEurUah);
        var sut = Service(new FixedProvider("UAH", new() { [Friday] = 0.0263m }));

        var result = await sut.ConvertToUsdAsync(100m, "UAH", Sunday);

        result.Amount.Should().Be(2.63m);
        result.Status.Should().Be(FxStatus.NearestPublished);
    }

    [Fact]
    public async Task ConvertToUsdAsync_IsTodayRate_WhenNoFeedPublishedAndTheLiveRateIsInForce()
    {
        using var _ = FxTableScope.Live(LiveEurUah);
        var sut = Service(); // no provider supports anything

        var result = await sut.ConvertToUsdAsync(100m, "UAH", Friday);

        result.Amount.Should().Be(2.5m);
        result.Status.Should().Be(FxStatus.TodayRate);
    }

    [Fact]
    public async Task ConvertToUsdAsync_IsSeedTable_WhenTheTodayRateIsItselfTheSeed()
    {
        using var _ = FxTableScope.Seed();
        var sut = Service();

        var result = await sut.ConvertToUsdAsync(100m, "UAH", Friday);

        result.Amount.Should().Be(2.4m);
        result.Status.Should().Be(FxStatus.SeedTable);
    }

    [Fact]
    public async Task ConvertToUsdAsync_IsUnknown1To1_ForAnUnknownCurrency_WithoutThrowing()
    {
        using var _ = FxTableScope.Live(LiveEurUah);
        var sut = Service();

        var result = await sut.ConvertToUsdAsync(42m, "XYZ", Friday);

        result.Amount.Should().Be(42m);
        result.Status.Should().Be(FxStatus.Unknown1To1);
    }

    [Fact]
    public async Task ConvertToUsdAsync_IsExact_ForUsd_WithoutAskingAFeed()
    {
        using var _ = FxTableScope.Seed();
        var sut = Service();

        var result = await sut.ConvertToUsdAsync(42m, "usd", Friday);

        result.Should().Be(new ConvertResult(42m, FxStatus.Exact));
    }

    [Fact]
    public async Task ConvertToUsdAsync_IsUnknown1To1_ForABlankCurrency()
    {
        var sut = Service();

        var result = await sut.ConvertToUsdAsync(42m, "", Friday);

        result.Should().Be(new ConvertResult(42m, FxStatus.Unknown1To1));
    }

    [Fact]
    public async Task ConvertToUsdAsync_ReadsAFeedThroughTheNbuProvider_AsExact()
    {
        using var _ = FxTableScope.Live(LiveEurUah);
        const string body = """[{"exchangedate":"05.01.2024","r030":840,"cc":"USD","rate":40.0,"units":1}]""";
        var http = new HttpClient(new Handler(body)) { BaseAddress = new Uri("https://bank.gov.ua/") };
        var sut = Service(new NbuHistoricalExchangeRateProvider(
            http, NullLogger<NbuHistoricalExchangeRateProvider>.Instance));

        var result = await sut.ConvertToUsdAsync(400m, "UAH", Friday);

        result.Amount.Should().Be(10m);
        result.Status.Should().Be(FxStatus.Exact);
    }

    private sealed class Handler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }
}
