using System.Web;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Binance;
using FinanceSentry.Tests.Unit.CryptoSync.RevolutX;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Microsoft.Extensions.Options;

namespace FinanceSentry.Tests.Unit.CryptoSync;

/// <summary>
/// The Binance coin walk (S2b) over <c>/sapi/v1/capital/deposit/hisrec</c> and <c>/sapi/v1/capital/withdraw/history</c>
/// responses in the documented shape: statuses, hash and network, the window under 90 days, paging and the pending hold.
/// </summary>
public class BinanceChainFlowWalkTests
{
    private const string DepositsPath = "/sapi/v1/capital/deposit/hisrec";
    private const string WithdrawalsPath = "/sapi/v1/capital/withdraw/history";
    private const string PricesPath = "/api/v3/ticker/price";
    private const string ApiKey = "testapikeytestapikeytestapikeytestapikeytestapikey123456";
    private const string ApiSecret = "testsecrettestsecrettestsecrettestsecrettestsecret123456";
    private static readonly DateTime From = new(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime AsOf = new(2027, 3, 20, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Mar4 = new(2027, 3, 4, 0, 20, 0, DateTimeKind.Utc);
    private static readonly long Mar4Ms = new DateTimeOffset(Mar4).ToUnixTimeMilliseconds();

    private StubHttpMessageHandler _handler = new();
    private Func<int, string> _deposits = _ => "[]";
    private Func<int, string> _withdrawals = _ => "[]";

    public BinanceChainFlowWalkTests()
    {
        _handler.Respond(DepositsPath, request => _deposits(Offset(request)));
        _handler.Respond(WithdrawalsPath, request => _withdrawals(Offset(request)));
        _handler.Respond(PricesPath, """[{"symbol": "SOLUSDT", "price": "150.00"}, {"symbol": "BTCUSDT", "price": "60000.00"}]""");
    }

    private static int Offset(HttpRequestMessage request) =>
        int.Parse(HttpUtility.ParseQueryString(request.RequestUri!.Query)["offset"]!);

    private BinanceAdapter CreateSut()
    {
        var options = Options.Create(new BinanceOptions());
        return new BinanceAdapter(
            new BinanceHttpClient(new HttpClient(_handler), options),
            new BinanceHoldingsAggregator(),
            NullLogger<BinanceAdapter>.Instance,
            options);
    }

    private Task<CryptoFlowPage> WalkAsync(DateTime? from = null, DateTime? asOf = null) =>
        CreateSut().GetChainFlowsAsync(ApiKey, ApiSecret, new CryptoFlowWalk(from ?? From, asOf ?? AsOf));

    private static string Deposit(string id, string amount, int status, long inserted, long? completed = null,
        string coin = "SOL", string txId = "H12", int transferType = 0) =>
        $$"""{"id": "{{id}}", "amount": "{{amount}}", "coin": "{{coin}}", "network": "SOL", "status": {{status}}, "address": "addr", "txId": "{{txId}}", "insertTime": {{inserted}}, "completeTime": {{(completed is { } c ? c.ToString() : "null")}}, "transferType": {{transferType}}, "confirmTimes": "1/1", "unlockConfirm": 0, "walletType": 0}""";

    private static string Withdrawal(string id, string amount, string fee, int status, string applied, string coin = "BTC",
        string txId = "0xAB01", int transferType = 0) =>
        $$"""{"id": "{{id}}", "amount": "{{amount}}", "transactionFee": "{{fee}}", "coin": "{{coin}}", "status": {{status}}, "address": "addr", "txId": "{{txId}}", "applyTime": "{{applied}}", "network": "BTC", "transferType": {{transferType}}, "completeTime": "{{applied}}"}""";

    [Fact]
    public async Task ACreditedDepositAndACompletedWithdrawal_AreReadWithHashNetworkFeeAndPrice()
    {
        _deposits = _ => $"[{Deposit("dep-1", "10.00", 1, Mar4Ms - 60_000, Mar4Ms)}]";
        _withdrawals = _ => $"[{Withdrawal("wd-1", "0.1", "0.0002", 6, "2027-03-05 08:00:00")}]";

        var page = await WalkAsync();

        page.Flows.Select(f => (f.EventId, f.ProviderType, f.OccurredAt, f.Amount, f.Unit, f.IsFiat, f.UnitPriceUsd, f.Fee, f.FeeUnit,
            f.CounterpartyRef, f.Status, f.TxRef, f.Network)).Should().Equal(
            ("dep-1", CryptoFlowTypes.CryptoDeposit, Mar4, 10.00m, "SOL", false, (decimal?)150m, (decimal?)null, (string?)null,
                BinanceAdapter.OnChainCounterparty, "1", "H12", "SOL"),
            ("wd-1", CryptoFlowTypes.CryptoWithdrawal, new DateTime(2027, 3, 5, 8, 0, 0, DateTimeKind.Utc), 0.1m, "BTC", false,
                (decimal?)60000m, (decimal?)0.0002m, "BTC", BinanceAdapter.OnChainCounterparty, "6", "0xAB01", "BTC"));
        page.WalkedTo.Should().Be(AsOf, "a window shorter than 90 days ends at the run, read inclusively");
    }

    [Fact]
    public async Task ACreditedButLockedDeposit_IsMoney_AndAFailedOrRejectedLegMovedNothing()
    {
        _deposits = _ => $"[{Deposit("locked", "5", 6, Mar4Ms)}, {Deposit("rejected", "5", 2, Mar4Ms)}, {Deposit("wrong", "5", 7, Mar4Ms)}]";
        _withdrawals = _ => $"[{Withdrawal("refused", "1", "0", 3, "2027-03-05 08:00:00")}, {Withdrawal("failed", "1", "0", 5, "2027-03-05 08:00:00")}]";

        var page = await WalkAsync();

        page.Flows.Should().ContainSingle().Which.EventId.Should().Be("locked");
        page.WalkedTo.Should().Be(AsOf);
    }

    [Fact]
    public async Task ATransferWithAnotherBinanceAccount_IsMarkedInternalToBinance()
    {
        _deposits = _ => $"[{Deposit("int-1", "5", 1, Mar4Ms, txId: "Off-chain transfer 6ad1", transferType: 1)}]";

        var flow = (await WalkAsync()).Flows.Should().ContainSingle().Subject;

        flow.CounterpartyRef.Should().Be(BinanceAdapter.InternalCounterparty);
        flow.TxRef.Should().Be("Off-chain transfer 6ad1");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public async Task APendingDeposit_HoldsTheCursorJustBeforeIt_SoItIsReadAgainOnceCredited(int status)
    {
        _deposits = _ => $"[{Deposit("pending", "5", status, Mar4Ms)}, {Deposit("done", "1", 1, Mar4Ms - 1000, Mar4Ms - 500)}]";

        var page = await WalkAsync();

        page.Flows.Should().ContainSingle().Which.EventId.Should().Be("done");
        page.WalkedTo.Should().Be(Mar4.AddMilliseconds(-1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task APendingWithdrawal_HoldsTheCursorJustBeforeIt(int status)
    {
        _withdrawals = _ => $"[{Withdrawal("pending", "1", "0", status, "2027-03-05 08:00:00")}]";

        var page = await WalkAsync();

        page.Flows.Should().BeEmpty();
        page.WalkedTo.Should().Be(new DateTime(2027, 3, 5, 8, 0, 0, DateTimeKind.Utc).AddMilliseconds(-1));
        _handler.Requests.Should().NotContain(r => r.RequestUri!.AbsolutePath == PricesPath, "nothing completed needs a price");
    }

    [Fact]
    public async Task APendingLegOlderThanTheHold_NoLongerPinsTheCursor()
    {
        _deposits = _ => $"[{Deposit("stuck", "5", 0, Mar4Ms)}]";

        var page = await WalkAsync(asOf: Mar4.AddDays(31));

        page.WalkedTo.Should().Be(Mar4.AddDays(31));
    }

    [Fact]
    public async Task AWalkFarBehind_ReadsOneWindowUnder90Days_AndResumesAfterIt()
    {
        var asOf = From.AddDays(200);

        var page = await WalkAsync(asOf: asOf);

        var query = HttpUtility.ParseQueryString(_handler.Requests.First(r => r.RequestUri!.AbsolutePath == DepositsPath).RequestUri!.Query);
        var start = long.Parse(query["startTime"]!);
        var end = long.Parse(query["endTime"]!);
        TimeSpan.FromMilliseconds(end - start).Should().BeLessThan(TimeSpan.FromDays(90));
        page.WalkedTo.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(end).UtcDateTime);
        _handler.Requests.Select(r => r.RequestUri!.AbsolutePath).Should().BeEquivalentTo(new[] { DepositsPath, WithdrawalsPath });
    }

    [Fact]
    public async Task AFullPage_IsFollowedByTheNextOffset()
    {
        var full = string.Join(",", Enumerable.Range(0, 1000).Select(i => Deposit($"d{i}", "1", 1, Mar4Ms, Mar4Ms + i)));
        _deposits = offset => offset == 0 ? $"[{full}]" : $"[{Deposit("last", "1", 1, Mar4Ms, Mar4Ms + 5000)}]";

        var page = await WalkAsync();

        page.Flows.Should().HaveCount(1001);
        _handler.Requests.Where(r => r.RequestUri!.AbsolutePath == DepositsPath).Select(Offset).Should().Equal(0, 1000);
    }

    [Fact]
    public async Task ARefusedHistory_Throws_SoTheCursorStays()
    {
        _handler = new StubHttpMessageHandler()
            .Respond(DepositsPath, """{"code": -1022, "msg": "Signature for this request is not valid."}""", System.Net.HttpStatusCode.Unauthorized)
            .Respond(WithdrawalsPath, "[]");

        var act = () => WalkAsync();

        await act.Should().ThrowAsync<CryptoExchangeException>();
    }

    [Fact]
    public async Task ACursorPastTheRun_ReadsNothing()
    {
        var page = await WalkAsync(from: AsOf.AddMilliseconds(1));

        page.Flows.Should().BeEmpty();
        _handler.Requests.Should().BeEmpty();
    }
}
