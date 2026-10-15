namespace FinanceSentry.Tests.Unit.Wealth;

using System.Security.Claims;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Headlines;
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

    private static Task<NetWorthHistoryResponse> GetHistoryAsync(
        string baseCurrency, StubPublishedRates rates, params NetWorthSnapshotDto[] snapshots) =>
        GetHistoryAsync(baseCurrency, rates, null, snapshots);

    private static async Task<NetWorthHistoryResponse> GetHistoryAsync(
        string baseCurrency, StubPublishedRates rates, Headline? changeHeadline, NetWorthSnapshotDto[] snapshots)
    {
        var handler = new Mock<IQueryHandler<GetNetWorthHistoryQuery, NetWorthHistoryResponse>>();
        handler.Setup(h => h.Handle(It.IsAny<GetNetWorthHistoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NetWorthHistoryResponse(snapshots, snapshots.Length > 0, null, changeHeadline));
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
    public async Task GetHistory_ConvertsEachSourcesTotalAtTheRowsRate_AndKeepsItsHealthAndReasons()
    {
        using var _ = FxTableScope.Seed();
        var rates = Eur((Friday, 1.10m));
        var day = Day(Friday) with
        {
            Sources =
            [
                new NetWorthSourceDto("banking", "Bank", 1_100m, HeadlineQuality.Complete, []),
                new NetWorthSourceDto("crypto", "CryptoVenue", 0m, HeadlineQuality.Degraded, ["STALE_SOURCE"]),
                new NetWorthSourceDto("inzhur", "Fund", null, HeadlineQuality.Unavailable, ["STALE_SOURCE"]),
            ],
        };

        var row = (await GetHistoryAsync("EUR", rates, day)).Snapshots.Single();

        row.Sources!.Select(s => s.Total).Should().Equal(1_100m / 1.10m, 0m, null);
        row.Sources!.Select(s => s.Health).Should().Equal(
            HeadlineQuality.Complete, HeadlineQuality.Degraded, HeadlineQuality.Unavailable);
        row.Sources![1].Reasons.Should().Equal("STALE_SOURCE");
        row.Sources![0].Total.Should().Be(row.TotalNetWorth, "the one counted source is the whole row");
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

    // ---- The change headline is read off the converted end rows ----

    private static Headline UsdChange(NetWorthSnapshotDto first, NetWorthSnapshotDto last, bool scopeChanged = false) =>
        HeadlineFactory.Change(
            first.TotalNetWorth, last.TotalNetWorth, HeadlineScope.Umbrella,
            first.SnapshotDate, last.SnapshotDate, scopeChanged, HeadlineSignals.None);

    [Fact]
    public async Task GetHistory_StatesTheChangeAsLastRowMinusFirstRow_WhenTheRateMovesOnAFlatUsdNetWorth()
    {
        using var _ = FxTableScope.Seed();
        var rates = Eur((Friday, 1.10m), (Monday, 1.20m));
        var first = Day(Friday, 100_000m);
        var last = Day(Monday, 100_000m);

        var result = await GetHistoryAsync("EUR", rates, UsdChange(first, last), [first, last]);

        var headline = result.ChangeHeadline!;
        var start = 100_000m / 1.10m;
        var end = 100_000m / 1.20m;
        result.Snapshots[1].TotalNetWorth.Should().Be(end);
        headline.Currency.Should().Be("EUR");
        headline.Amount.Should().Be(end - start).And.BeLessThan(0m);
        headline.Percent.Should().Be(Math.Round((end - start) / start * 100m, 2)).And.BeLessThan(0m);
        headline.Reasons.Should().NotContain(r => r.Code == HeadlineReasonCode.FxTodayRate);
    }

    [Fact]
    public async Task GetHistory_KeepsTheChangeAndWithholdsThePercent_WhenTheScopeChangedInAnotherBase()
    {
        using var _ = FxTableScope.Seed();
        var rates = Eur((Friday, 1.10m), (Monday, 1.20m));
        var first = Day(Friday, 100_000m);
        var last = Day(Monday, 120_000m);

        var result = await GetHistoryAsync("EUR", rates, UsdChange(first, last, scopeChanged: true), [first, last]);

        result.ChangeHeadline!.Amount.Should().Be(120_000m / 1.20m - 100_000m / 1.10m);
        result.ChangeHeadline.Percent.Should().BeNull();
    }

    [Fact]
    public async Task GetHistory_CarriesTheWorstEndRowStatusOnTheChange_WhenAnEndRowFellBackToTodaysRate()
    {
        using var _ = FxTableScope.Seed();
        var rates = Eur((Monday, 1.20m));
        var first = Day(Friday, 100_000m);
        var last = Day(Monday, 100_000m);

        var result = await GetHistoryAsync("EUR", rates, UsdChange(first, last), [first, last]);

        result.ChangeHeadline!.Amount.Should().Be(100_000m / 1.20m - CurrencyConverter.FromUsd(100_000m, "EUR"));
        result.ChangeHeadline.Reasons.Should().Contain(r => r.Code == HeadlineReasonCode.FxTodayRate);
    }

    [Fact]
    public async Task GetHistory_LeavesTheUsdChangeUntouched_WhenTheBaseIsUsd()
    {
        var first = Day(Friday, 100_000m);
        var last = Day(Monday, 110_000m);
        var usd = UsdChange(first, last);

        var result = await GetHistoryAsync("USD", Eur((Friday, 1.10m)), usd, [first, last]);

        result.ChangeHeadline.Should().Be(usd);
    }
}
