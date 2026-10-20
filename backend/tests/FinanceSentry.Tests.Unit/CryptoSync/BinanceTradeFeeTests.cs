using System.Net;
using System.Web;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Binance;
using FinanceSentry.Tests.Unit.CryptoSync.RevolutX;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Microsoft.Extensions.Options;

namespace FinanceSentry.Tests.Unit.CryptoSync;

/// <summary>
/// The commission on a Binance spot fill (<c>/api/v3/myTrades</c>) reaching <see cref="CryptoTrade"/>, valued in USD at
/// the fill's own time: report fixture 14 (ETH 2,000, BNB 600).
/// </summary>
public class BinanceTradeFeeTests
{
    private const string ApiKey = "testapikeytestapikeytestapikeytestapikeytestapikey123456";
    private const string ApiSecret = "testsecrettestsecrettestsecrettestsecrettestsecret123456";
    private static readonly DateTime Mar6 = new(2027, 3, 6, 9, 0, 0, DateTimeKind.Utc);
    private static readonly long Mar6Ms = new DateTimeOffset(Mar6).ToUnixTimeMilliseconds();
    private static readonly CryptoTradeWalk Walk = new(Mar6.AddDays(-5), Mar6.AddDays(1));

    private readonly StubHttpMessageHandler _handler = new();

    private BinanceAdapter CreateSut()
    {
        var options = Options.Create(new BinanceOptions());
        return new BinanceAdapter(
            new BinanceHttpClient(new HttpClient(_handler), options),
            new BinanceHoldingsAggregator(),
            NullLogger<BinanceAdapter>.Instance,
            options);
    }

    private const long DayMs = 86_400_000;

    private static string Fill(long id, string price, string qty, string commission, string commissionAsset, bool isBuyer = true, long? timeMs = null) =>
        $$"""{"id": {{id}}, "symbol": "ETHUSDT", "price": "{{price}}", "qty": "{{qty}}", "quoteQty": "2000.00", "commission": "{{commission}}", "commissionAsset": "{{commissionAsset}}", "time": {{timeMs ?? Mar6Ms}}, "isBuyer": {{isBuyer.ToString().ToLowerInvariant()}}}""";

    private static string Row(long id, string price, string qty, string commission, string commissionAsset, bool isBuyer = true) =>
        $"[{Fill(id, price, qty, commission, commissionAsset, isBuyer)}]";

    private static string DailyCandle(string close) =>
        $$"""[[0, "590.00", "601.00", "589.00", "{{close}}", "10.0", 0, "6000.0", 12, "5.0", "3000.0", "0"]]""";

    private void RespondWithFills(params string[] fills) =>
        _handler.Respond("/api/v3/myTrades", request =>
            HttpUtility.ParseQueryString(request.RequestUri!.Query)["symbol"] == "ETHUSDT" ? $"[{string.Join(',', fills)}]" : "[]");

    private async Task<CryptoTrade> SingleTradeAsync(string row)
    {
        _handler.Respond("/api/v3/myTrades", request =>
            HttpUtility.ParseQueryString(request.RequestUri!.Query)["symbol"] == "ETHUSDT" ? row : "[]");
        var page = await CreateSut().GetTradesAsync(ApiKey, ApiSecret, "ETH", cursor: null, Walk);
        return page.Trades.Should().ContainSingle().Subject;
    }

    [Fact]
    public async Task Fixture14_CommissionInBnb_IsValuedAtBnbsDailyClosePriceOnTheTradeDate()
    {
        _handler.Respond("/api/v3/klines", request =>
        {
            var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
            query["symbol"].Should().Be("BNBUSDT");
            query["interval"].Should().Be("1d");
            query["startTime"].Should().Be((Mar6Ms - (Mar6Ms % DayMs)).ToString());
            return DailyCandle("600.00");
        });

        var trade = await SingleTradeAsync(Row(1, "2000.00", "1.0", "0.005", "BNB"));

        trade.Fee.Should().Be(0.005m);
        trade.FeeAsset.Should().Be("BNB");
        trade.FeeUsd.Should().Be(3.00m);
    }

    [Fact]
    public async Task CommissionInTheTradedAsset_IsValuedAtTheFillsPrice_WithoutAPriceCall()
    {
        var trade = await SingleTradeAsync(Row(1, "2000.00", "1.0", "0.001", "ETH"));

        trade.FeeUsd.Should().Be(2.00m);
        _handler.Requests.Should().NotContain(r => r.RequestUri!.AbsolutePath == "/api/v3/klines");
    }

    [Fact]
    public async Task CommissionInAStablecoin_IsValuedAtPar()
    {
        var trade = await SingleTradeAsync(Row(1, "2000.00", "1.0", "2.0", "USDT", isBuyer: false));

        trade.FeeUsd.Should().Be(2.0m);
    }

    [Fact]
    public async Task CommissionInAnAssetWithNoPrice_IsKeptButUnvalued()
    {
        _handler.RespondWithStatus("/api/v3/klines", _ => (HttpStatusCode.BadRequest, "{\"code\":-1121,\"msg\":\"Invalid symbol.\"}"));

        var trade = await SingleTradeAsync(Row(1, "2000.00", "1.0", "0.005", "BNB"));

        trade.Fee.Should().Be(0.005m);
        trade.FeeAsset.Should().Be("BNB");
        trade.FeeUsd.Should().BeNull();
    }

    [Fact]
    public async Task FeesPaidInTheSameAssetOnTheSameDay_ShareOneDailyPriceCall()
    {
        var nextDayMs = Mar6Ms + DayMs;
        RespondWithFills(
            Fill(1, "2000.00", "1.0", "0.005", "BNB"),
            Fill(2, "2000.00", "1.0", "0.010", "BNB", timeMs: Mar6Ms + 3 * 60_000),
            Fill(3, "2000.00", "1.0", "0.010", "BNB", timeMs: nextDayMs));
        _handler.Respond("/api/v3/klines", request =>
            DailyCandle(HttpUtility.ParseQueryString(request.RequestUri!.Query)["startTime"] == (Mar6Ms - (Mar6Ms % DayMs)).ToString() ? "600.00" : "700.00"));

        var page = await CreateSut().GetTradesAsync(ApiKey, ApiSecret, "ETH", cursor: null, new CryptoTradeWalk(Mar6.AddDays(-5), Mar6.AddDays(3)));

        page.Trades.Select(t => t.FeeUsd).Should().Equal(3.00m, 6.00m, 7.00m);
        _handler.Requests.Count(r => r.RequestUri!.AbsolutePath == "/api/v3/klines").Should().Be(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task TransientPriceFailureMidWalk_KeepsTheValuedFillsAndResumesFromTheFailedOne(HttpStatusCode status)
    {
        RespondWithFills(
            Fill(1, "2000.00", "1.0", "2.0", "USDT"),
            Fill(2, "2000.00", "1.0", "0.005", "BNB"),
            Fill(3, "2000.00", "1.0", "2.0", "USDT"));
        var failing = true;
        _handler.RespondWithStatus("/api/v3/klines", _ => failing
            ? (status, "{\"code\":-1003,\"msg\":\"Too many requests.\"}")
            : (HttpStatusCode.OK, DailyCandle("600.00")));

        var first = await CreateSut().GetTradesAsync(ApiKey, ApiSecret, "ETH", cursor: null, Walk);

        first.Trades.Select(t => t.TradeId).Should().Equal("1");
        first.IsComplete.Should().BeFalse();
        first.NextCursor.Should().Be("USDT=2");

        failing = false;
        var second = await CreateSut().GetTradesAsync(ApiKey, ApiSecret, "ETH", first.NextCursor, Walk);

        second.Trades.Select(t => t.TradeId).Should().Equal("2", "3");
        second.Trades[0].FeeUsd.Should().Be(3.00m);
        second.IsComplete.Should().BeTrue();
        second.NextCursor.Should().Be("USDT=4");
    }

    [Fact]
    public async Task TransientPriceFailureOnTheFirstFill_StoresNothingAndKeepsTheCursor()
    {
        RespondWithFills(Fill(5, "2000.00", "1.0", "0.005", "BNB"));
        _handler.Respond("/api/v3/klines", "{}", HttpStatusCode.TooManyRequests);

        var page = await CreateSut().GetTradesAsync(ApiKey, ApiSecret, "ETH", cursor: "USDT=5", Walk);

        page.Trades.Should().BeEmpty();
        page.IsComplete.Should().BeFalse();
        page.NextCursor.Should().Be("USDT=5");
    }

    [Fact]
    public async Task ZeroCommission_CarriesNoFee()
    {
        var trade = await SingleTradeAsync(Row(1, "2000.00", "1.0", "0.00000000", "BNB"));

        trade.Fee.Should().BeNull();
        trade.FeeAsset.Should().BeNull();
        trade.FeeUsd.Should().BeNull();
    }
}
