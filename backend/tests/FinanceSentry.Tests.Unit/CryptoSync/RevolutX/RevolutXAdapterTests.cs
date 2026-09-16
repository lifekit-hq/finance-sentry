using System.Net;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using FinanceSentry.Modules.CryptoSync.Infrastructure.RevolutX;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceSentry.Tests.Unit.CryptoSync.RevolutX;

/// <summary>The adapter end to end over recorded-shape responses: signing, three calls, aggregation.</summary>
public class RevolutXAdapterTests
{
    private readonly StubHttpMessageHandler _handler = new StubHttpMessageHandler()
        .Respond("/api/1.0/balances", RevolutXFixtures.Balances)
        .Respond("/api/1.0/configuration/currencies", RevolutXFixtures.Currencies)
        .Respond("/api/1.0/tickers", RevolutXFixtures.Tickers);

    private RevolutXAdapter CreateSut()
    {
        var configuration = new ConfigurationBuilder().Build();
        return new RevolutXAdapter(
            new RevolutXHttpClient(new HttpClient(_handler), configuration, TimeProvider.System),
            new RevolutXHoldingsAggregator(),
            NullLogger<RevolutXAdapter>.Instance,
            configuration);
    }

    private static readonly string ApiKey = new('k', 64);

    [Fact]
    public void ExchangeName_IsTheRevolutXSlug()
    {
        CreateSut().ExchangeName.Should().Be(CryptoExchangeProvider.RevolutX).And.Be("revolut_x");
    }

    [Fact]
    public async Task GetHoldings_ReturnsPricedCryptoHoldings_AndVenueFiat()
    {
        var holdings = await CreateSut().GetHoldingsAsync(ApiKey, RevolutXTestKeys.Ed25519PrivateKeyPem);

        holdings.Select(h => (h.Asset, h.IsFiat)).Should().Equal(
            ("BTC", false), ("ETH", false), ("DOGE", false), ("USDC", false), ("EUR", true), ("USD", true));
        _handler.Requests.Select(r => r.RequestUri!.AbsolutePath).Should().BeEquivalentTo(
            "/api/1.0/balances", "/api/1.0/configuration/currencies", "/api/1.0/tickers");
        _handler.Requests.Should().OnlyContain(r => r.Method == HttpMethod.Get, "the key is read-only");
    }

    [Fact]
    public async Task ValidateCredentials_MakesALiveSignedBalancesCall()
    {
        await CreateSut().ValidateCredentialsAsync(ApiKey, RevolutXTestKeys.Ed25519PrivateKeyPem);

        var request = _handler.Requests.Should().ContainSingle().Which;
        request.RequestUri!.AbsolutePath.Should().Be("/api/1.0/balances");
        request.Headers.Contains(RevolutXSigner.SignatureHeader).Should().BeTrue();
    }

    [Fact]
    public async Task ValidateCredentials_RejectedByTheVenue_Throws()
    {
        _handler.Respond("/api/1.0/balances", RevolutXFixtures.Unauthorized, HttpStatusCode.Unauthorized);

        var act = () => CreateSut().ValidateCredentialsAsync(ApiKey, RevolutXTestKeys.Ed25519PrivateKeyPem);

        await act.Should().ThrowAsync<RevolutXException>();
    }

    [Fact]
    public async Task ValidateCredentials_UnusableKey_FailsBeforeAnyNetworkCall()
    {
        var act = () => CreateSut().ValidateCredentialsAsync(ApiKey, "not a key");

        await act.Should().ThrowAsync<RevolutXException>();
        _handler.Requests.Should().BeEmpty();
    }

    private static readonly DateTime AsOf = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly long AsOfMs = new DateTimeOffset(AsOf).ToUnixTimeMilliseconds();
    private const long DayMs = 24L * 60 * 60 * 1000;
    private const long WeekMs = 7 * DayMs;

    private static CryptoTradeWalk Walk(TimeSpan sinceConnect) => new(AsOf - sinceConnect, AsOf);

    private static Dictionary<string, string> Query(HttpRequestMessage request) =>
        request.RequestUri!.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));

    private List<HttpRequestMessage> TradeRequests(string symbol) =>
        _handler.Requests.Where(r => r.RequestUri!.AbsolutePath == $"/api/1.0/trades/private/{symbol}").ToList();

    private Task<CryptoTradePage> WalkBtcAsync(string? cursor, CryptoTradeWalk walk) =>
        CreateSut().GetTradesAsync(ApiKey, RevolutXTestKeys.Ed25519PrivateKeyPem, "btc", cursor, walk);

    [Fact]
    public void TradeHistory_StartsAtConnect()
    {
        CreateSut().TradeHistoryStartsAtConnect.Should().BeTrue();
    }

    [Fact]
    public async Task GetTrades_WalksEveryUsdValuedPairOfTheAsset_AndPricesFillsInUsd()
    {
        var at = AsOfMs - DayMs;
        _handler
            .Respond("/api/1.0/configuration/pairs", RevolutXFixtures.Pairs)
            .Respond("/api/1.0/trades/private/BTC-USD", _ => RevolutXFixtures.TradesPage(null,
                RevolutXFixtures.Fill("aa01", "BTC", "USD", "60000.50", "0.10000000", "buy", at)))
            .Respond("/api/1.0/trades/private/BTC-EUR", _ => RevolutXFixtures.TradesPage(null,
                RevolutXFixtures.Fill("bb01", "BTC", "EUR", "50000", "0.02", "sell", at + 1)));

        var page = await WalkBtcAsync(cursor: null, Walk(TimeSpan.FromDays(2)));

        page.Trades.Should().BeEquivalentTo(
        [
            new CryptoTrade("aa01", "BTC", "USD", 0.1m, 60_000.50m, 6_000.05m, IsBuyer: true,
                DateTimeOffset.FromUnixTimeMilliseconds(at).UtcDateTime),
            new CryptoTrade("bb01", "BTC", "EUR", 0.02m, CurrencyConverter.ToUsd(50_000m, "EUR"),
                CurrencyConverter.ToUsd(1_000m, "EUR"), IsBuyer: false,
                DateTimeOffset.FromUnixTimeMilliseconds(at + 1).UtcDateTime),
        ], o => o.WithStrictOrdering());
        TradeRequests("BTC-ETH").Should().BeEmpty("a crypto-quoted fill cannot be priced in USD");
        _handler.Requests.Should().NotContain(r => r.RequestUri!.AbsolutePath.Contains("ETH-USD"));
        _handler.Requests.Should().OnlyContain(r => r.Method == HttpMethod.Get, "the key is read-only");
        page.NextCursor.Should().Be(RevolutXTradeCursor.Format(AsOfMs));
        page.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task GetTrades_NeverAsksForMoreThanAWeek_AndCoversConnectToAsOfWithoutGaps()
    {
        _handler
            .Respond("/api/1.0/configuration/pairs", RevolutXFixtures.Pairs)
            .Respond("/api/1.0/trades/private/BTC-USD", _ => RevolutXFixtures.TradesPage(null))
            .Respond("/api/1.0/trades/private/BTC-EUR", _ => RevolutXFixtures.TradesPage(null));
        var walk = Walk(TimeSpan.FromDays(10));

        await WalkBtcAsync(cursor: null, walk);

        var windows = TradeRequests("BTC-USD")
            .Select(Query)
            .Select(q => (Start: long.Parse(q["start_date"]), End: long.Parse(q["end_date"])))
            .ToList();
        windows.Should().HaveCount(2);
        windows.Should().OnlyContain(w => w.End - w.Start <= WeekMs);
        var connectMs = new DateTimeOffset(walk.TrackedSince).ToUnixTimeMilliseconds();
        windows[0].Start.Should().BeLessThan(connectMs);
        windows[1].Start.Should().BeLessThanOrEqualTo(windows[0].End, "consecutive windows leave no gap");
        windows[1].End.Should().BeGreaterThan(AsOfMs);
        TradeRequests("BTC-EUR").Should().HaveCount(2);
    }

    [Fact]
    public async Task GetTrades_AFillOnAWindowBoundary_IsCountedOnce()
    {
        var connectMs = AsOfMs - 10 * DayMs;
        var firstWindowEnd = connectMs + WeekMs - 3;
        var boundaryFill = RevolutXFixtures.Fill("cc01", "BTC", "USD", "100", "1", "buy", firstWindowEnd);
        _handler
            .Respond("/api/1.0/configuration/pairs", RevolutXFixtures.Pairs)
            // The venue returns the boundary fill to both windows (inclusive bounds on both).
            .Respond("/api/1.0/trades/private/BTC-USD", _ => RevolutXFixtures.TradesPage(null, boundaryFill))
            .Respond("/api/1.0/trades/private/BTC-EUR", _ => RevolutXFixtures.TradesPage(null));

        var page = await WalkBtcAsync(cursor: null, Walk(TimeSpan.FromDays(10)));

        page.Trades.Should().ContainSingle().Which.TradeId.Should().Be("cc01");
    }

    [Fact]
    public async Task GetTrades_FollowsTheVenueCursorWithinAWindow()
    {
        var at = AsOfMs - DayMs;
        _handler
            .Respond("/api/1.0/configuration/pairs", RevolutXFixtures.Pairs)
            .Respond("/api/1.0/trades/private/BTC-USD", request => Query(request).GetValueOrDefault("cursor") switch
            {
                null => RevolutXFixtures.TradesPage("page/2+",
                    RevolutXFixtures.Fill("dd01", "BTC", "USD", "100", "1", "buy", at)),
                "page/2+" => RevolutXFixtures.TradesPage(null,
                    RevolutXFixtures.Fill("dd02", "BTC", "USD", "100", "1", "sell", at + 5)),
                _ => throw new InvalidOperationException("unexpected cursor"),
            })
            .Respond("/api/1.0/trades/private/BTC-EUR", _ => RevolutXFixtures.TradesPage(null));

        var page = await WalkBtcAsync(cursor: null, Walk(TimeSpan.FromDays(2)));

        page.Trades.Select(t => t.TradeId).Should().Equal("dd01", "dd02");
        TradeRequests("BTC-USD").Should().HaveCount(2);
    }

    [Fact]
    public async Task GetTrades_ResumesJustAfterTheCursor()
    {
        _handler
            .Respond("/api/1.0/configuration/pairs", RevolutXFixtures.Pairs)
            .Respond("/api/1.0/trades/private/BTC-USD", _ => RevolutXFixtures.TradesPage(null))
            .Respond("/api/1.0/trades/private/BTC-EUR", _ => RevolutXFixtures.TradesPage(null));
        var walkedTo = AsOfMs - DayMs;
        var fillAtWatermark = RevolutXFixtures.Fill("ee01", "BTC", "USD", "100", "1", "buy", walkedTo);
        _handler.Respond("/api/1.0/trades/private/BTC-USD", _ => RevolutXFixtures.TradesPage(null, fillAtWatermark));

        var page = await WalkBtcAsync(RevolutXTradeCursor.Format(walkedTo), Walk(TimeSpan.FromDays(300)));

        page.Trades.Should().BeEmpty("the fill at the watermark was counted by the previous walk");
        var query = Query(TradeRequests("BTC-USD").Single());
        long.Parse(query["start_date"]).Should().Be(walkedTo);
        page.NextCursor.Should().Be(RevolutXTradeCursor.Format(AsOfMs));
    }

    [Fact]
    public async Task GetTrades_CursorAlreadyAtAsOf_MakesNoCall()
    {
        var page = await WalkBtcAsync(RevolutXTradeCursor.Format(AsOfMs), Walk(TimeSpan.FromDays(1)));

        page.Trades.Should().BeEmpty();
        page.NextCursor.Should().Be(RevolutXTradeCursor.Format(AsOfMs));
        page.IsComplete.Should().BeTrue();
        _handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTrades_FarBehind_StopsAfterHalfAYear_AndReportsItIsIncomplete()
    {
        _handler
            .Respond("/api/1.0/configuration/pairs", RevolutXFixtures.Pairs)
            .Respond("/api/1.0/trades/private/BTC-USD", _ => RevolutXFixtures.TradesPage(null))
            .Respond("/api/1.0/trades/private/BTC-EUR", _ => RevolutXFixtures.TradesPage(null));

        var page = await WalkBtcAsync(cursor: null, Walk(TimeSpan.FromDays(400)));

        page.IsComplete.Should().BeFalse();
        TradeRequests("BTC-USD").Should().HaveCount(26);
        RevolutXTradeCursor.Parse(page.NextCursor).Should().BeLessThan(AsOfMs);
        var last = Query(TradeRequests("BTC-USD")[^1]);
        RevolutXTradeCursor.Parse(page.NextCursor).Should().Be(long.Parse(last["end_date"]) - 1);
    }

    [Fact]
    public async Task GetTrades_UnreadableFill_IsSkipped_NotGuessed()
    {
        var at = AsOfMs - DayMs;
        _handler
            .Respond("/api/1.0/configuration/pairs", RevolutXFixtures.Pairs)
            .Respond("/api/1.0/trades/private/BTC-USD", _ => RevolutXFixtures.TradesPage(null,
                RevolutXFixtures.Fill("ff01", "BTC", "USD", "", "1", "buy", at),
                RevolutXFixtures.Fill("ff02", "BTC", "USD", "100", "1", "transfer", at),
                RevolutXFixtures.Fill("ff03", "BTC", "USD", "1,5", "1", "buy", at),
                RevolutXFixtures.Fill("ff04", "BTC", "USD", "100", "0.5", "buy", at)))
            .Respond("/api/1.0/trades/private/BTC-EUR", _ => RevolutXFixtures.TradesPage(null));

        var page = await WalkBtcAsync(cursor: null, Walk(TimeSpan.FromDays(2)));

        page.Trades.Select(t => t.TradeId).Should().Equal("ff04");
    }

    [Fact]
    public async Task GetTrades_VenueFailure_Throws_SoTheCursorStays()
    {
        _handler
            .Respond("/api/1.0/configuration/pairs", RevolutXFixtures.Pairs)
            .Respond("/api/1.0/trades/private/BTC-EUR", _ => RevolutXFixtures.TradesPage(null))
            .Respond("/api/1.0/trades/private/BTC-USD", RevolutXFixtures.Unauthorized, HttpStatusCode.TooManyRequests);

        var act = () => WalkBtcAsync(cursor: null, Walk(TimeSpan.FromDays(2)));

        (await act.Should().ThrowAsync<RevolutXException>()).Which.VenueStatusCode.Should().Be(429);
    }
}
