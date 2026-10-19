using System.Net;
using System.Web;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Binance;
using FinanceSentry.Tests.Unit.CryptoSync.RevolutX;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

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
        var config = new ConfigurationBuilder().Build();
        return new BinanceAdapter(
            new BinanceHttpClient(new HttpClient(_handler), config),
            new BinanceHoldingsAggregator(),
            NullLogger<BinanceAdapter>.Instance,
            config);
    }

    private static string Row(long id, string price, string qty, string commission, string commissionAsset, bool isBuyer = true) =>
        $$"""[{"id": {{id}}, "symbol": "ETHUSDT", "price": "{{price}}", "qty": "{{qty}}", "quoteQty": "2000.00", "commission": "{{commission}}", "commissionAsset": "{{commissionAsset}}", "time": {{Mar6Ms}}, "isBuyer": {{isBuyer.ToString().ToLowerInvariant()}}}]""";

    private async Task<CryptoTrade> SingleTradeAsync(string row)
    {
        _handler.Respond("/api/v3/myTrades", request =>
            HttpUtility.ParseQueryString(request.RequestUri!.Query)["symbol"] == "ETHUSDT" ? row : "[]");
        var page = await CreateSut().GetTradesAsync(ApiKey, ApiSecret, "ETH", cursor: null, Walk);
        return page.Trades.Should().ContainSingle().Subject;
    }

    [Fact]
    public async Task Fixture14_CommissionInBnb_IsValuedAtBnbsPriceOnTheTradeMinute()
    {
        _handler.Respond("/api/v3/klines", request =>
        {
            var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
            query["symbol"].Should().Be("BNBUSDT");
            query["startTime"].Should().Be((Mar6Ms - (Mar6Ms % 60_000)).ToString());
            return $$"""[[{{Mar6Ms}}, "600.00", "601.00", "599.00", "600.50", "10.0", {{Mar6Ms + 59_999}}, "6000.0", 12, "5.0", "3000.0", "0"]]""";
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

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task TransientPriceFailure_PropagatesInsteadOfStoringTheFeeUnvalued(HttpStatusCode status)
    {
        _handler.Respond("/api/v3/myTrades", Row(1, "2000.00", "1.0", "0.005", "BNB"));
        _handler.Respond("/api/v3/klines", "{}", status);

        var act = () => CreateSut().GetTradesAsync(ApiKey, ApiSecret, "ETH", cursor: null, Walk);

        await act.Should().ThrowAsync<BinanceException>();
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
