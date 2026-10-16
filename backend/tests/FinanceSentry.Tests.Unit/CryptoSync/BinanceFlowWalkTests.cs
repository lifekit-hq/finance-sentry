using System.Web;
using FinanceSentry.Modules.CryptoSync.Domain;
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
/// The Binance fiat-leg walk over <c>/sapi/v1/fiat/orders</c> and <c>/sapi/v1/fiat/payments</c> responses in the
/// documented shape: deposits, withdrawals, card buys and sells, with their fee, paging and the pending hold.
/// </summary>
public class BinanceFlowWalkTests
{
    private const string OrdersPath = "/sapi/v1/fiat/orders";
    private const string PaymentsPath = "/sapi/v1/fiat/payments";
    private const string ApiKey = "testapikeytestapikeytestapikeytestapikeytestapikey123456";
    private const string ApiSecret = "testsecrettestsecrettestsecrettestsecrettestsecret123456";
    private static readonly DateTime From = new(2027, 4, 9, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime AsOf = new(2027, 4, 20, 0, 0, 0, DateTimeKind.Utc);
    private static readonly long Apr9 = new DateTimeOffset(2027, 4, 9, 10, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
    private static readonly DateTime WindowEnd = From.AddDays(1).AddMilliseconds(-1);

    private const int OrdersIn = 0;
    private const int PaymentsIn = 1;
    private const int OrdersOut = 2;
    private const int PaymentsOut = 3;

    private readonly StubHttpMessageHandler _handler = new();
    private readonly Dictionary<(string Path, string Type), Func<int, string>> _pages = [];

    public BinanceFlowWalkTests()
    {
        foreach (var path in new[] { OrdersPath, PaymentsPath })
        {
            _handler.Respond(path, request =>
            {
                var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
                var page = int.Parse(query["page"]!);
                return _pages.TryGetValue((path, query["transactionType"]!), out var body) ? body(page) : Page(0);
            });
        }
    }

    private BinanceAdapter CreateSut()
    {
        var config = new ConfigurationBuilder().Build();
        return new BinanceAdapter(
            new BinanceHttpClient(new HttpClient(_handler), config),
            new BinanceHoldingsAggregator(),
            NullLogger<BinanceAdapter>.Instance,
            config);
    }

    private Task<CryptoFlowPage> WalkAsync(int lane = OrdersIn, DateTime? from = null, DateTime? asOf = null) =>
        CreateSut().GetFlowsAsync(ApiKey, ApiSecret, new CryptoFlowWalk(from ?? From, asOf ?? AsOf, lane));

    private static string Page(int total, params string[] rows) =>
        $$"""{"code": "000000", "message": "success", "data": [{{string.Join(",", rows)}}], "total": {{total}}, "success": true}""";

    private static string Order(string orderNo, string indicated, string amount, string fee, string status, long created) =>
        $$"""{"orderNo": "{{orderNo}}", "fiatCurrency": "EUR", "indicatedAmount": "{{indicated}}", "amount": "{{amount}}", "totalFee": "{{fee}}", "method": "BankAccount", "status": "{{status}}", "createTime": {{created}}, "updateTime": {{created + 1000}}}""";

    private static string Payment(string orderNo, string source, string fee, string method, string status, long created) =>
        $$"""{"orderNo": "{{orderNo}}", "sourceAmount": "{{source}}", "fiatCurrency": "EUR", "obtainAmount": "0.0016", "cryptoCurrency": "BTC", "totalFee": "{{fee}}", "price": "61250", "status": "{{status}}", "paymentMethod": "{{method}}", "createTime": {{created}}, "updateTime": {{created}}}""";

    [Fact]
    public async Task EachLane_IsOneCallOnItsOwnEndpointAndDirection_AndReadsItsLegsWithTheirFee()
    {
        _pages[(OrdersPath, "0")] = _ => Page(1, Order("dep-1", "300.00", "299.00", "1.00", "Successful", Apr9));
        _pages[(PaymentsPath, "0")] = _ => Page(1, Payment("buy-1", "100.0", "2.0", "Credit Card", "Completed", Apr9));
        _pages[(OrdersPath, "1")] = _ => Page(1, Order("wd-1", "50.00", "49.50", "0.50", "Finished", Apr9));
        _pages[(PaymentsPath, "1")] = _ => Page(1, Payment("sell-1", "80.0", "0", "Cash Balance", "Completed", Apr9));

        var rows = new List<(int Lane, string Path, string Type, CryptoFlow Flow)>();
        foreach (var lane in new[] { OrdersIn, PaymentsIn, OrdersOut, PaymentsOut })
        {
            _handler.Requests.Clear();
            var page = await WalkAsync(lane);
            var request = _handler.Requests.Where(r => r.RequestUri!.AbsolutePath.StartsWith("/sapi/v1/fiat", StringComparison.Ordinal))
                .Should().ContainSingle("a run spends one weighted fiat call").Subject;
            var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
            rows.Add((lane, request.RequestUri.AbsolutePath, query["transactionType"]!, page.Flows.Should().ContainSingle().Subject));
        }

        rows.Select(r => (r.Lane, r.Path, r.Type, r.Flow.EventId, r.Flow.ProviderType, r.Flow.Amount, r.Flow.Fee, r.Flow.CounterpartyRef)).Should().Equal(
            (OrdersIn, OrdersPath, "0", "dep-1", CryptoFlowTypes.FiatDeposit, 300m, (decimal?)1m, "BankAccount"),
            (PaymentsIn, PaymentsPath, "0", "buy-1", CryptoFlowTypes.CardBuy, 100m, (decimal?)2m, "Credit Card"),
            (OrdersOut, OrdersPath, "1", "wd-1", CryptoFlowTypes.FiatWithdrawal, 49.50m, (decimal?)0.5m, "BankAccount"),
            (PaymentsOut, PaymentsPath, "1", "sell-1", CryptoFlowTypes.CardSell, 80m, (decimal?)null, "Cash Balance"));
        rows.Should().OnlyContain(r => r.Flow.IsFiat && r.Flow.Unit == "EUR");
        _handler.Requests.Should().OnlyContain(r => r.Method == HttpMethod.Get, "the key is read-only");
    }

    [Theory]
    [InlineData(OrdersIn)]
    [InlineData(PaymentsIn)]
    [InlineData(OrdersOut)]
    public async Task AnEarlierLane_HoldsTheCursor_AndHandsOnTheNextLane(int lane)
    {
        var page = await WalkAsync(lane);

        page.WalkedTo.Should().Be(From.AddMilliseconds(-1));
        page.NextLane.Should().Be(lane + 1);
    }

    [Fact]
    public async Task TheLastLane_MovesTheCursorToTheEndOfTheWindow_AndStartsTheRotationOver()
    {
        var page = await WalkAsync(PaymentsOut);

        page.WalkedTo.Should().Be(WindowEnd);
        page.NextLane.Should().Be(OrdersIn);
    }

    [Fact]
    public async Task AWindowNotYetOver_IsNotCalled_AndKeepsItsLane()
    {
        var page = await WalkAsync(PaymentsIn, asOf: From.AddHours(23));

        page.Flows.Should().BeEmpty();
        page.WalkedTo.Should().Be(From.AddMilliseconds(-1));
        page.NextLane.Should().Be(PaymentsIn);
        _handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task FailedAndExpiredLegs_MovedNothing()
    {
        _pages[(OrdersPath, "0")] = _ => Page(2,
            Order("exp-1", "10.00", "10.00", "0.00", "Expired", Apr9),
            Order("fail-1", "10.00", "10.00", "0.00", "Failed", Apr9));

        var page = await WalkAsync();

        page.Flows.Should().BeEmpty();
    }

    [Theory]
    [InlineData(OrdersIn, OrdersPath, "0")]
    [InlineData(PaymentsOut, PaymentsPath, "1")]
    public async Task APendingLeg_HoldsTheWindow_AndRestartsTheLanes_SoItIsReadAgainOnceSettled(int lane, string path, string type)
    {
        _pages[(path, type)] = _ => Page(2,
            path == OrdersPath
                ? Order("done-1", "300.00", "300.00", "0.00", "Successful", Apr9 - 3_600_000)
                : Payment("done-1", "300.00", "0", "Credit Card", "Completed", Apr9 - 3_600_000),
            path == OrdersPath
                ? Order("pend-1", "100.00", "100.00", "0.00", "Processing", Apr9)
                : Payment("pend-1", "100.00", "0", "Credit Card", "Processing", Apr9));

        var page = await WalkAsync(lane);

        page.Flows.Select(f => f.EventId).Should().Equal("done-1");
        page.WalkedTo.Should().Be(From.AddMilliseconds(-1));
        page.NextLane.Should().Be(OrdersIn);
    }

    [Fact]
    public async Task APendingLegOlderThanTheHold_NoLongerPinsTheWindow()
    {
        var stale = new DateTimeOffset(AsOf.AddDays(-31)).ToUnixTimeMilliseconds();
        var from = AsOf.AddDays(-31).Date;
        _pages[(OrdersPath, "0")] = _ => Page(1, Order("stuck-1", "100.00", "100.00", "0.00", "Processing", stale));

        var page = await WalkAsync(OrdersIn, from);

        page.NextLane.Should().Be(PaymentsIn);
    }

    [Fact]
    public async Task AFullPage_IsFollowedByTheNext()
    {
        var full = Enumerable.Range(0, 500)
            .Select(i => Order($"dep-{i}", "1.00", "1.00", "0.00", "Successful", Apr9 + i)).ToArray();
        _pages[(OrdersPath, "0")] = page => page == 1
            ? Page(501, full)
            : Page(501, Order("dep-500", "1.00", "1.00", "0.00", "Successful", Apr9 + 500));

        var result = await WalkAsync();

        result.Flows.Should().HaveCount(501);
    }

    [Fact]
    public async Task ARefusedHistory_Throws_SoTheCursorAndLaneStay()
    {
        _pages[(PaymentsPath, "1")] = _ => """{"code": "100001", "message": "refused", "data": [], "total": 0, "success": false}""";

        var act = () => WalkAsync(PaymentsOut);

        await act.Should().ThrowAsync<BinanceException>();
    }

    [Fact]
    public async Task TheLastLane_AlsoReadsRewardsAirdropsAndDust_PricedAtTheVenueRate()
    {
        _handler
            .Respond("/api/v3/ticker/price", """[{"symbol":"BNBUSDT","price":"500"}]""")
            .Respond("/sapi/v1/simple-earn/flexible/history/rewardsRecord", request =>
                HttpUtility.ParseQueryString(request.RequestUri!.Query)["type"] == "REWARDS"
                    ? $$"""{"rows":[{"asset":"USDT","rewards":"3.00","projectId":"USDT001","type":"REWARDS","time":{{Apr9}}}],"total":1}"""
                    : """{"rows":[],"total":0}""")
            .Respond("/sapi/v1/simple-earn/locked/history/rewardsRecord",
                $$"""{"rows":[{"positionId":"7","asset":"USDT","amount":"1.5","time":{{Apr9}}}],"total":1}""")
            .Respond("/sapi/v1/asset/assetDividend",
                $$"""{"rows":[{"id":11,"tranId":901,"asset":"BNB","amount":"0.1","enInfo":"Airdrop distribution","divTime":{{Apr9}}},{"id":12,"tranId":902,"asset":"USDT","amount":"9","enInfo":"Simple Earn payout","divTime":{{Apr9}}}],"total":2}""")
            .Respond("/sapi/v1/asset/dribblet",
                $$"""{"total":1,"userAssetDribblets":[{"transId":55,"operateTime":{{Apr9}},"totalTransferedAmount":"0.2","totalServiceChargeAmount":"0.0001"}]}""");

        var page = await WalkAsync(PaymentsOut);

        page.Flows.Select(f => (f.EventId, f.ProviderType, f.Amount, f.Unit, f.UnitPriceUsd, f.Fee)).Should().BeEquivalentTo(
        [
            ("USDT001:REWARDS:" + Apr9, CryptoFlowTypes.Reward, 3.00m, "USDT", (decimal?)1m, (decimal?)null),
            ("7:" + Apr9, CryptoFlowTypes.Reward, 1.5m, "USDT", (decimal?)1m, (decimal?)null),
            ("901", CryptoFlowTypes.Airdrop, 0.1m, "BNB", (decimal?)500m, (decimal?)null),
            ("55", CryptoFlowTypes.DustConversion, 0.2m, "BNB", (decimal?)500m, (decimal?)0.0001m),
        ]);
        page.WalkedTo.Should().Be(WindowEnd);
    }

    [Fact]
    public async Task ARejectedEarnSource_IsSkipped_AndTheWindowStillCompletes()
    {
        _handler.Respond("/sapi/v1/asset/dribblet", """{"code":-2015,"msg":"Invalid API-key, IP, or permissions for action."}""",
            System.Net.HttpStatusCode.Unauthorized);

        var page = await WalkAsync(PaymentsOut);

        page.Flows.Should().BeEmpty();
        page.WalkedTo.Should().Be(WindowEnd);
    }
}
