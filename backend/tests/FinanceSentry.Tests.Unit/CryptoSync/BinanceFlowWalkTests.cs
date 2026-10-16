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
    private static readonly DateTime From = new(2027, 4, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime AsOf = new(2027, 4, 20, 0, 0, 0, DateTimeKind.Utc);
    private static readonly long Apr9 = new DateTimeOffset(2027, 4, 9, 10, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

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

    private Task<CryptoFlowPage> WalkAsync(DateTime? from = null) =>
        CreateSut().GetFlowsAsync(ApiKey, ApiSecret, new CryptoFlowWalk(from ?? From, AsOf));

    private static string Page(int total, params string[] rows) =>
        $$"""{"code": "000000", "message": "success", "data": [{{string.Join(",", rows)}}], "total": {{total}}, "success": true}""";

    private static string Order(string orderNo, string indicated, string amount, string fee, string status, long created) =>
        $$"""{"orderNo": "{{orderNo}}", "fiatCurrency": "EUR", "indicatedAmount": "{{indicated}}", "amount": "{{amount}}", "totalFee": "{{fee}}", "method": "BankAccount", "status": "{{status}}", "createTime": {{created}}, "updateTime": {{created + 1000}}}""";

    private static string Payment(string orderNo, string source, string fee, string method, string status, long created) =>
        $$"""{"orderNo": "{{orderNo}}", "sourceAmount": "{{source}}", "fiatCurrency": "EUR", "obtainAmount": "0.0016", "cryptoCurrency": "BTC", "totalFee": "{{fee}}", "price": "61250", "status": "{{status}}", "paymentMethod": "{{method}}", "createTime": {{created}}, "updateTime": {{created}}}""";

    [Fact]
    public async Task FiatLegs_AreReadWithTheirFee_DepositAtTheIndicatedAmount_WithdrawalAtWhatLeft()
    {
        _pages[(OrdersPath, "0")] = _ => Page(1, Order("dep-1", "300.00", "299.00", "1.00", "Successful", Apr9 - 86_400_000));
        _pages[(OrdersPath, "1")] = _ => Page(1, Order("wd-1", "50.00", "49.50", "0.50", "Finished", Apr9 + 86_400_000));
        _pages[(PaymentsPath, "0")] = _ => Page(1, Payment("buy-1", "100.0", "2.0", "Credit Card", "Completed", Apr9));
        _pages[(PaymentsPath, "1")] = _ => Page(1, Payment("sell-1", "80.0", "0", "Cash Balance", "Completed", Apr9 + 3_600_000));

        var page = await WalkAsync();

        page.Flows.Select(f => (f.EventId, f.ProviderType, f.Amount, f.Unit, f.Fee, f.FeeUnit, f.CounterpartyRef, f.IsFiat)).Should().Equal(
            ("dep-1", CryptoFlowTypes.FiatDeposit, 300m, "EUR", 1m, "EUR", "BankAccount", true),
            ("buy-1", CryptoFlowTypes.CardBuy, 100m, "EUR", 2m, "EUR", "Credit Card", true),
            ("sell-1", CryptoFlowTypes.CardSell, 80m, "EUR", (decimal?)null, (string?)null, "Cash Balance", true),
            ("wd-1", CryptoFlowTypes.FiatWithdrawal, 49.50m, "EUR", 0.5m, "EUR", "BankAccount", true));
        page.WalkedTo.Should().Be(AsOf);
        _handler.Requests.Should().OnlyContain(r => r.Method == HttpMethod.Get, "the key is read-only");
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

    [Fact]
    public async Task APendingLeg_HoldsTheCursorBeforeIt()
    {
        _pages[(OrdersPath, "0")] = _ => Page(2,
            Order("dep-1", "300.00", "300.00", "0.00", "Successful", Apr9 - 86_400_000),
            Order("dep-2", "100.00", "100.00", "0.00", "Processing", Apr9));

        var page = await WalkAsync();

        page.Flows.Select(f => f.EventId).Should().Equal("dep-1");
        page.WalkedTo.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(Apr9 - 1).UtcDateTime);
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
    public async Task OneRunReadsOne30DayWindow_FromTheCursor()
    {
        var page = await WalkAsync(from: AsOf.AddDays(-75));

        page.WalkedTo.Should().Be(AsOf.AddDays(-45).AddMilliseconds(-1));
        _handler.Requests.Should().HaveCount(4, "orders and payments, each in and out");
    }

    [Fact]
    public async Task ARefusedHistory_Throws_SoTheCursorStays()
    {
        _pages[(PaymentsPath, "1")] = _ => """{"code": "100001", "message": "refused", "data": [], "total": 0, "success": false}""";

        var act = () => WalkAsync();

        await act.Should().ThrowAsync<BinanceException>();
    }
}
