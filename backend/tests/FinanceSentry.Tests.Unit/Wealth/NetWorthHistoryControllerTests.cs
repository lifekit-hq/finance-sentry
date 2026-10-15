namespace FinanceSentry.Tests.Unit.Wealth;

using System.Security.Claims;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Modules.Wealth.API.Controllers;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FinanceSentry.Tests.Unit.Fx;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

public class NetWorthHistoryControllerTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static Task<NetWorthHistoryResponse> GetHistoryAsync(string baseCurrency, params NetWorthSnapshotDto[] snapshots) =>
        GetHistoryAsync(baseCurrency, new StubPublishedRates(), snapshots);

    private static async Task<NetWorthHistoryResponse> GetHistoryAsync(
        string baseCurrency, StubPublishedRates rates, params NetWorthSnapshotDto[] snapshots)
    {
        var handler = new Mock<IQueryHandler<GetNetWorthHistoryQuery, NetWorthHistoryResponse>>();
        handler.Setup(h => h.Handle(It.IsAny<GetNetWorthHistoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NetWorthHistoryResponse(snapshots, snapshots.Length > 0));
        var currencies = new Mock<IUserBaseCurrencyReader>();
        currencies.Setup(c => c.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(baseCurrency);

        var controller = new NetWorthHistoryController(handler.Object, currencies.Object, rates)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserId.ToString())], "test")),
                },
            },
        };

        var result = await controller.GetHistory();
        return result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<NetWorthHistoryResponse>().Subject;
    }

    [Fact]
    public async Task GetHistory_ConvertsTheCashAndInvestedSplitWithTheTotals_WhenTheBaseCurrencyIsNotUsd()
    {
        var day = new NetWorthSnapshotDto(
            new DateOnly(2026, 10, 2), 1_000m, 500m, 250m, 1_750m, "USD", null, false,
            CashTotal: 1_150m, BrokerageInvested: 400m, CryptoInvested: 200m);

        var usd = await GetHistoryAsync("USD", day);
        var eur = await GetHistoryAsync("EUR", day);

        var rate = CurrencyConverter.FromUsd(1m, "EUR");
        rate.Should().NotBe(1m, "the test needs a base currency whose rate differs from USD");
        var row = eur.Snapshots.Single();
        row.Currency.Should().Be("EUR");
        row.TotalNetWorth.Should().Be(CurrencyConverter.FromUsd(1_750m, "EUR"));
        row.CashTotal.Should().Be(CurrencyConverter.FromUsd(1_150m, "EUR"));
        row.BrokerageInvested.Should().Be(CurrencyConverter.FromUsd(400m, "EUR"));
        row.CryptoInvested.Should().Be(CurrencyConverter.FromUsd(200m, "EUR"));
        (row.CashTotal + row.BrokerageInvested + row.CryptoInvested).Should().BeApproximately(row.TotalNetWorth, 0.0001m);
        usd.Snapshots.Single().CashTotal.Should().Be(1_150m);
    }

    [Fact]
    public async Task GetHistory_KeepsADayWithNoSplitNull_WhenTheBaseCurrencyIsNotUsd()
    {
        var day = new NetWorthSnapshotDto(new DateOnly(2026, 5, 1), 900m, 0m, 0m, 900m, "USD", null, false);

        var row = (await GetHistoryAsync("EUR", day)).Snapshots.Single();

        row.CashTotal.Should().BeNull();
        row.BrokerageInvested.Should().BeNull();
        row.CryptoInvested.Should().BeNull();
    }

    // ---- Dated rates (C4): each row converts at the rate published for its own date ----

    // Fictional USD-per-EUR quotes. 2026-03-06 is a Friday; the feed skips the weekend.
    private static readonly DateOnly Friday = new(2026, 3, 6);
    private static readonly DateOnly Saturday = new(2026, 3, 7);
    private static readonly DateOnly Monday = new(2026, 3, 9);

    private static NetWorthSnapshotDto Day(DateOnly date, decimal usd = 1_100m, params string[] reasons) =>
        new(date, usd, 0m, 0m, usd, "USD", null, false, CashTotal: usd, FxReasons: reasons);

    private static StubPublishedRates Eur(params (DateOnly Date, decimal Rate)[] quotes) =>
        new(new() { ["EUR"] = quotes.ToDictionary(q => q.Date, q => q.Rate) });

    [Fact]
    public async Task GetHistory_ConvertsEachRowAtItsOwnDatesRate_WhenTheBaseCurrencyIsNotUsd()
    {
        using var _ = FxTableScope.Seed();
        var rates = Eur((Friday, 1.10m), (Monday, 1.20m));

        var rows = (await GetHistoryAsync("EUR", rates, Day(Friday), Day(Monday))).Snapshots;

        rows[0].TotalNetWorth.Should().Be(1_100m / 1.10m);
        rows[0].CashTotal.Should().Be(1_100m / 1.10m);
        rows[1].TotalNetWorth.Should().Be(1_100m / 1.20m);
        rows.Should().OnlyContain(r => r.Currency == "EUR" && r.FxReasons!.Count == 0);
    }

    [Fact]
    public async Task GetHistory_AsksForTheWholeRangeInOneLookup()
    {
        using var _ = FxTableScope.Seed();
        var rates = Eur((Friday, 1.10m), (Monday, 1.20m));

        await GetHistoryAsync("EUR", rates, Day(Friday), Day(Saturday), Day(Monday));

        rates.SeriesCalls.Should().Be(1);
        rates.LastFrom.Should().Be(Friday);
        rates.LastTo.Should().Be(Monday);
    }

    [Fact]
    public async Task GetHistory_UsesTheNearestEarlierRate_AndSaysNearestPublished_WhenTheDateHasNone()
    {
        using var _ = FxTableScope.Seed();
        var rates = Eur((Friday, 1.10m));

        var row = (await GetHistoryAsync("EUR", rates, Day(Saturday))).Snapshots.Single();

        row.TotalNetWorth.Should().Be(1_100m / 1.10m);
        row.FxReasons.Should().Equal("FX_NEAREST_PUBLISHED");
    }

    [Fact]
    public async Task GetHistory_BoundsTheNearestLookback_AtSevenDays()
    {
        using var _ = FxTableScope.Seed();
        var rates = Eur((Friday, 1.10m));
        var withinBound = Friday.AddDays(CachingHistoricalExchangeRateService.PublishedLookbackDays);
        var pastBound = withinBound.AddDays(1);

        var rows = (await GetHistoryAsync("EUR", rates, Day(withinBound), Day(pastBound))).Snapshots;

        rows[0].TotalNetWorth.Should().Be(1_100m / 1.10m);
        rows[0].FxReasons.Should().Equal("FX_NEAREST_PUBLISHED");
        rows[1].TotalNetWorth.Should().Be(CurrencyConverter.FromUsd(1_100m, "EUR"));
        rows[1].FxReasons.Should().Equal("FX_TODAY_RATE");
    }

    [Fact]
    public async Task GetHistory_FallsBackToTodaysRate_AndSaysTodayRate_WhenNothingIsPublished()
    {
        using var _ = FxTableScope.Seed();

        var row = (await GetHistoryAsync("EUR", new StubPublishedRates(), Day(Friday))).Snapshots.Single();

        row.TotalNetWorth.Should().Be(CurrencyConverter.FromUsd(1_100m, "EUR"));
        row.FxReasons.Should().Equal("FX_TODAY_RATE");
    }

    [Fact]
    public async Task GetHistory_KeepsTheWorseStoredReason_OverANearestPublishedOne()
    {
        using var _ = FxTableScope.Seed();
        var rates = Eur((Friday, 1.10m));

        var rows = (await GetHistoryAsync(
            "EUR", rates, Day(Saturday, 1_100m, "FX_TODAY_RATE"), Day(Saturday, 1_100m, "FX_UNKNOWN_1TO1"))).Snapshots;

        rows[0].FxReasons.Should().Equal("FX_TODAY_RATE");
        rows[1].FxReasons.Should().Equal("FX_UNKNOWN_1TO1");
    }

    [Fact]
    public async Task GetHistory_ShowsATodayRowAtTheLiveRate_WithoutALookup()
    {
        using var _ = FxTableScope.Live(new Dictionary<string, decimal> { ["EUR"] = 1.09m });
        var rates = Eur();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var row = (await GetHistoryAsync("EUR", rates, Day(today))).Snapshots.Single();

        row.TotalNetWorth.Should().Be(CurrencyConverter.FromUsd(1_100m, "EUR"));
        row.FxReasons.Should().BeEmpty();
        rates.SeriesCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetHistory_LeavesAUsdBaseUntouched_WithoutALookup()
    {
        var rates = Eur((Friday, 1.10m));
        var day = Day(Friday);

        var result = await GetHistoryAsync("USD", rates, day);

        result.Snapshots.Single().Should().Be(day);
        rates.SeriesCalls.Should().Be(0);
    }
}
