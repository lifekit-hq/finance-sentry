using System.Web;
using FinanceSentry.Modules.CryptoSync.Application.Services;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using FinanceSentry.Modules.CryptoSync.Infrastructure.RevolutX;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Microsoft.Extensions.Options;

namespace FinanceSentry.Tests.Unit.CryptoSync.RevolutX;

/// <summary>
/// The Revolut X money-movement walk over <c>/transactions</c> responses in the documented shape (the
/// TransactionsPaginatedResponse example): receive and send legs, their counterparty account type, paging and the
/// pending hold; and, for a transfer with an external crypto wallet, its chain hash, network and fee from
/// <c>/transactions/{id}</c> (the TransactionDetails example).
/// </summary>
public class RevolutXFlowWalkTests
{
    private const string TransactionsPath = "/api/1.0/transactions";
    private static readonly string ApiKey = new('k', 64);
    private static readonly DateTime From = new(2023, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime AsOf = new(2023, 11, 20, 0, 0, 0, DateTimeKind.Utc);

    private const string SendToWalletId = "c3d4e5f6-a7b8-9012-cdef-234567890123";

    private readonly StubHttpMessageHandler _handler = new StubHttpMessageHandler()
        .Respond("/api/1.0/configuration/currencies", RevolutXFixtures.Currencies)
        .Respond("/api/1.0/tickers", RevolutXFixtures.Tickers)
        .Respond($"{TransactionsPath}/{SendToWalletId}", Details(SendToWalletId, "send", "0.5", fee: null, hash: null));

    private static string Details(string id, string type, string amount, string? fee, string? hash, string? network = null)
    {
        var feePart = fee is null ? "" : $", \"fee\": \"{fee}\", \"fee_currency\": \"BTC\"";
        var hashPart = hash is null ? "" : $", \"crypto_transaction_hash\": \"{hash}\"";
        var networkPart = network is null ? "" : $", \"network\": \"{network}\"";
        var source = $$"""{"amount": "{{amount}}", "currency": "BTC"{{feePart}}, "account": {"type": "revolut_x"} }""";
        return $$"""{"id": "{{id}}", "status": "completed", "type": "{{type}}", "source": {{source}}, "destination": {"amount": "0.5", "currency": "BTC", "account": {"type": "external_crypto_wallet"} }, "created_date": 1700000200000{{hashPart}}{{networkPart}} }""";
    }

    private RevolutXAdapter CreateSut()
    {
        var options = Options.Create(new RevolutXOptions());
        return new RevolutXAdapter(
            new RevolutXHttpClient(new HttpClient(_handler), options, TimeProvider.System),
            new RevolutXHoldingsAggregator(),
            NullLogger<RevolutXAdapter>.Instance,
            options);
    }

    private Task<CryptoFlowPage> WalkAsync(DateTime? from = null, DateTime? asOf = null) =>
        CreateSut().GetFlowsAsync(ApiKey, RevolutXTestKeys.Ed25519PrivateKeyPem, new CryptoFlowWalk(from ?? From, asOf ?? AsOf));

    private static string Leg(string amount, string currency, string accountType) =>
        $$$"""{"amount": "{{{amount}}}", "currency": "{{{currency}}}", "account": {"type": "{{{accountType}}}"}}""";

    private static string Row(string id, string status, string type, string? source, string? destination, long created, long? processed)
    {
        var legs = (source is null ? "" : $", \"source\": {source}") + (destination is null ? "" : $", \"destination\": {destination}");
        var settled = processed is null ? "" : $", \"processed_date\": {processed}";
        return $$"""{"id": "{{id}}", "status": "{{status}}", "type": "{{type}}"{{legs}}, "created_date": {{created}}{{settled}}}""";
    }

    private static string Page(string? nextCursor, params string[] rows)
    {
        var cursor = nextCursor is null ? "" : $", \"next_cursor\": \"{nextCursor}\"";
        return $$$"""{"data": [{{{string.Join(",", rows)}}}], "metadata": {"timestamp": 1785313433816{{{cursor}}}}}""";
    }

    // EUR 500 in from the owner's Revolut app; 0.5 BTC out to an external wallet.
    private static readonly string ReceiveFromRevolut = Row(
        "a1b2c3d4-e5f6-7890-abcd-ef0123456789", "completed", "receive",
        Leg("500.00", "EUR", "revolut"), Leg("500.00", "EUR", "revolut_x"), 1700000000000, 1700000001000);

    private static readonly string SendToWallet = Row(
        SendToWalletId, "completed", "send",
        Leg("0.5", "BTC", "revolut_x"), Leg("0.5", "BTC", "external_crypto_wallet"), 1700000200000, 1700000300000);

    [Fact]
    public async Task ARejectedRewardRequest_IsSkipped_AndTheTransfersAreStillRecorded()
    {
        _handler.RespondWithStatus(TransactionsPath, request =>
            HttpUtility.ParseQueryString(request.RequestUri!.Query)["types"] == "reward"
                ? (System.Net.HttpStatusCode.BadRequest, """{"message":"unknown type"}""")
                : (System.Net.HttpStatusCode.OK, Page(null, ReceiveFromRevolut, SendToWallet)));

        var page = await WalkAsync();

        page.Flows.Select(f => f.ProviderType).Should().Equal(CryptoFlowTypes.Receive, CryptoFlowTypes.Send);
        page.WalkedTo.Should().Be(AsOf);
    }

    [Fact]
    public async Task AReward_IsReadFromItsDestinationLeg_AsIncomeWithNoCounterparty()
    {
        var reward = Row("d4e5f6a7-b8c9-0123-def0-345678901234", "completed", "reward",
            null, Leg("0.00005", "BTC", "revolut_x"), 1700000400000, 1700000500000);
        _handler.Respond(TransactionsPath, Page(null, reward));

        var page = await WalkAsync();

        page.Flows.Should().Equal(
            new CryptoFlow("d4e5f6a7-b8c9-0123-def0-345678901234", CryptoFlowTypes.Reward,
                DateTimeOffset.FromUnixTimeMilliseconds(1700000500000).UtcDateTime, 0.00005m, "BTC", false, 60000m, null, null,
                null, "completed"));
    }

    [Fact]
    public async Task ReceiveAndSend_AreReadFromTheVenuesOwnLeg_WithTheOtherSidesAccountType()
    {
        _handler.Respond(TransactionsPath, Page(null, ReceiveFromRevolut, SendToWallet));

        var page = await WalkAsync();

        page.Flows.Should().Equal(
            new CryptoFlow("a1b2c3d4-e5f6-7890-abcd-ef0123456789", CryptoFlowTypes.Receive,
                DateTimeOffset.FromUnixTimeMilliseconds(1700000001000).UtcDateTime, 500m, "EUR", true, null, null, null,
                "revolut", "completed"),
            new CryptoFlow("c3d4e5f6-a7b8-9012-cdef-234567890123", CryptoFlowTypes.Send,
                DateTimeOffset.FromUnixTimeMilliseconds(1700000300000).UtcDateTime, 0.5m, "BTC", false, 60000m, null, null,
                "external_crypto_wallet", "completed"));
        page.WalkedTo.Should().Be(AsOf);
    }

    [Fact]
    public async Task TheWalk_AsksOnlyForTransfers_WithSignedReadOnlyGets()
    {
        _handler.Respond(TransactionsPath, Page(null));

        await WalkAsync();

        var requests = _handler.Requests.Where(r => r.RequestUri!.AbsolutePath == TransactionsPath).ToList();
        requests.Select(r => HttpUtility.ParseQueryString(r.RequestUri!.Query)["types"])
            .Should().Equal("receive,send", "reward");
        foreach (var request in requests)
        {
            request.Method.Should().Be(HttpMethod.Get);
            request.Headers.Contains(RevolutXSigner.SignatureHeader).Should().BeTrue();
            var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
            query["limit"].Should().Be("1900");
            long.Parse(query["end_date"]!).Should().BeLessThan(long.Parse(query["start_date"]!) + 30L * 24 * 60 * 60 * 1000,
                "a request spans at most 30 days");
        }
    }

    [Fact]
    public async Task AWindowLongerThanAPage_FollowsTheCursor()
    {
        _handler.Respond(TransactionsPath, request =>
        {
            var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
            return query["types"] == "reward" ? Page(null)
                : query["cursor"] is null ? Page("next-1", ReceiveFromRevolut)
                : Page(null, SendToWallet);
        });

        var page = await WalkAsync();

        page.Flows.Select(f => f.ProviderType).Should().Equal(CryptoFlowTypes.Receive, CryptoFlowTypes.Send);
        _handler.Requests.Where(r => r.RequestUri!.AbsolutePath == TransactionsPath).Should().HaveCount(3);
    }

    [Fact]
    public async Task ALongGap_IsWalkedIn30DayWindows()
    {
        _handler.Respond(TransactionsPath, Page(null));

        var page = await WalkAsync(from: AsOf.AddDays(-75));

        _handler.Requests.Where(r => r.RequestUri!.AbsolutePath == TransactionsPath).Should().HaveCount(6);
        page.WalkedTo.Should().Be(AsOf);
    }

    [Fact]
    public async Task APendingTransfer_HoldsTheCursorBeforeIt_SoItIsReadAgainOnceSettled()
    {
        var pending = Row("pending-1", "pending", "send",
            Leg("0.5", "BTC", "revolut_x"), Leg("0.5", "BTC", "external_crypto_wallet"), 1700000100000, null);
        _handler.Respond(TransactionsPath, Page(null, ReceiveFromRevolut, pending, SendToWallet));

        var page = await WalkAsync();

        page.Flows.Should().HaveCount(2, "settled rows are recorded; the unique key drops them when the held window is read again");
        page.WalkedTo.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1700000100000 - 1).UtcDateTime);
    }

    [Fact]
    public async Task CancelledAndOtherTypes_AreNotMovements_AndAnUnreadableRowIsSkipped()
    {
        var cancelled = Row("cancelled-1", "cancelled", "send",
            Leg("1", "BTC", "revolut_x"), Leg("1", "BTC", "external_crypto_wallet"), 1700000050000, 1700000060000);
        var noAmount = Row("broken-1", "completed", "receive", Leg("", "EUR", "external_bank_account"), null, 1700000070000, 1700000080000);
        _handler.Respond(TransactionsPath, Page(null, cancelled, noAmount, ReceiveFromRevolut));

        var page = await WalkAsync();

        page.Flows.Select(f => f.EventId).Should().Equal("a1b2c3d4-e5f6-7890-abcd-ef0123456789");
    }

    [Fact]
    public async Task ARefusedCall_Throws_SoTheCursorStays()
    {
        _handler.Respond(TransactionsPath, RevolutXFixtures.Unauthorized, System.Net.HttpStatusCode.Unauthorized);

        var act = () => WalkAsync();

        await act.Should().ThrowAsync<RevolutXException>();
    }

    [Fact]
    public async Task ATransferWithAnExternalWallet_TakesItsHashNetworkAndFeeFromItsDetails()
    {
        // The TransactionDetails example: 0.5001 BTC left the account, 0.0001 of it Revolut's fee, 0.5 went on chain.
        var send = Row(SendToWalletId, "completed", "send",
            Leg("0.5001", "BTC", "revolut_x"), Leg("0.5", "BTC", "external_crypto_wallet"), 1700000200000, 1700000300000);
        _handler.Respond(TransactionsPath, Page(null, ReceiveFromRevolut, send));
        _handler.Respond($"{TransactionsPath}/{SendToWalletId}",
            Details(SendToWalletId, "send", "0.5001", fee: "0.0001", hash: "0xABCDEF0123", network: "BITCOIN"));

        var page = await WalkAsync();

        var flow = page.Flows.Should().ContainSingle(f => f.ProviderType == CryptoFlowTypes.Send).Subject;
        (flow.Amount, flow.Fee, flow.FeeUnit, flow.TxRef, flow.Network).Should().Be((0.5m, (decimal?)0.0001m, "BTC", "0xABCDEF0123", "BITCOIN"));
        _handler.Requests.Where(r => r.RequestUri!.AbsolutePath.StartsWith($"{TransactionsPath}/", StringComparison.Ordinal))
            .Should().ContainSingle("only a transfer with an external crypto wallet is looked up, not the fiat receive");
    }

    [Fact]
    public async Task ASendReportedAsExternalCrypto_TakesItsHashNetworkAndFeeFromItsDetails()
    {
        // The live API spells the external-wallet account type "external_crypto".
        var send = Row(SendToWalletId, "completed", "send",
            Leg("0.5001", "BTC", "revolut_x"), Leg("0.5", "BTC", "external_crypto"), 1700000200000, 1700000300000);
        _handler.Respond(TransactionsPath, Page(null, send));
        _handler.Respond($"{TransactionsPath}/{SendToWalletId}",
            Details(SendToWalletId, "send", "0.5001", fee: "0.0001", hash: "5VERv8NMvzbJ", network: "SOLANA"));

        var flow = (await WalkAsync()).Flows.Should().ContainSingle().Subject;

        (flow.CounterpartyRef, flow.TxRef, flow.Network, flow.Fee).Should().Be(("external_crypto", "5VERv8NMvzbJ", "SOLANA", (decimal?)0.0001m));
        CryptoFlowClassifier.ChainTransfer(CryptoFlowEvent.Create(Guid.NewGuid(), CryptoExchangeProvider.RevolutX, flow, DateTime.UtcNow))
            .Should().Be(CryptoTransferDirection.Out);
    }

    [Fact]
    public async Task AReceiveFromAnExternalWallet_KeepsItsAmount_AndTakesItsHash()
    {
        const string id = "d4e5f6a7-b8c9-0123-def0-345678901234";
        var receive = Row(id, "completed", "receive",
            Leg("0.5", "BTC", "external_crypto_wallet"), Leg("0.5", "BTC", "revolut_x"), 1700000200000, 1700000300000);
        _handler.Respond(TransactionsPath, Page(null, receive));
        _handler.Respond($"{TransactionsPath}/{id}", Details(id, "receive", "0.5", fee: null, hash: "5VERv8NMvzbJMEkV8xnrLkEaWRtSz2CosKDYjCJjBRnbJLgp8uirBgmQpjKhoR4tjF3ZpRzrFmBV6UjKdiSZkQUW", network: "SOLANA"));

        var flow = (await WalkAsync()).Flows.Should().ContainSingle().Subject;

        (flow.Amount, flow.Fee, flow.TxRef, flow.Network).Should().Be(
            (0.5m, (decimal?)null, "5VERv8NMvzbJMEkV8xnrLkEaWRtSz2CosKDYjCJjBRnbJLgp8uirBgmQpjKhoR4tjF3ZpRzrFmBV6UjKdiSZkQUW", "SOLANA"));
    }

    [Fact]
    public async Task ARefusedDetailsCall_Throws_SoTheCursorStays()
    {
        _handler.Respond(TransactionsPath, Page(null, SendToWallet));
        _handler.Respond($"{TransactionsPath}/{SendToWalletId}", RevolutXFixtures.Unauthorized, System.Net.HttpStatusCode.Unauthorized);

        var act = () => WalkAsync();

        await act.Should().ThrowAsync<RevolutXException>();
    }
}
