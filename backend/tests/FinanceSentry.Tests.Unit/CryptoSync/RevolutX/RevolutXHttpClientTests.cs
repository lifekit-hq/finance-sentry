using System.Net;
using FinanceSentry.Infrastructure.Observability.Hangfire;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using FinanceSentry.Modules.CryptoSync.Infrastructure.RevolutX;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FinanceSentry.Tests.Unit.CryptoSync.RevolutX;

public class RevolutXHttpClientTests
{
    private static readonly DateTimeOffset SigningInstant =
        DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(RevolutXTestKeys.OpenSslBalancesTimestamp));

    private readonly StubHttpMessageHandler _handler = new();

    private RevolutXHttpClient CreateSut(string? baseUrl = null) =>
        new(
            new HttpClient(_handler),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["RevolutX:BaseUrl"] = baseUrl })
                .Build(),
            new FixedTimeProvider(SigningInstant));

    private static RevolutXCredentials Credentials() =>
        RevolutXSigner.ParseCredentials(new string('k', 64), RevolutXTestKeys.Ed25519PrivateKeyPem);

    [Fact]
    public async Task GetBalances_SendsTheThreeRevolutXHeaders_SignedOverTheFullApiPath()
    {
        _handler.Respond("/api/1.0/balances", RevolutXFixtures.Balances);

        await CreateSut().GetBalancesAsync(Credentials());

        var request = _handler.Requests.Should().ContainSingle().Which;
        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri.Should().Be(new Uri("https://revx.revolut.com/api/1.0/balances"));
        request.Headers.GetValues(RevolutXSigner.ApiKeyHeader).Should().Equal(new string('k', 64));
        request.Headers.GetValues(RevolutXSigner.TimestampHeader).Should().Equal(RevolutXTestKeys.OpenSslBalancesTimestamp);
        request.Headers.GetValues(RevolutXSigner.SignatureHeader)
            .Should().ContainSingle()
            .Which.Should().Be(RevolutXTestKeys.OpenSslBalancesSignatureBase64,
                "the header must be byte-identical to what OpenSSL produces for the same payload");
    }

    [Fact]
    public async Task GetBalances_ParsesStringAmountsVerbatim()
    {
        _handler.Respond("/api/1.0/balances", RevolutXFixtures.Balances);

        var balances = await CreateSut().GetBalancesAsync(Credentials());

        balances.Should().HaveCount(8);
        balances[1].Should().BeEquivalentTo(new RevolutXBalance("ETH", "1.50000000", "0.00000000", "2.00000000", "1.50000000"));
        balances[0].Staked.Should().BeNull("staked is optional");
    }

    [Fact]
    public async Task GetCurrenciesAndTickers_ReadTheDocumentedShapes()
    {
        _handler
            .Respond("/api/1.0/configuration/currencies", RevolutXFixtures.Currencies)
            .Respond("/api/1.0/tickers", RevolutXFixtures.Tickers);
        var sut = CreateSut();

        var currencies = await sut.GetCurrenciesAsync(Credentials());
        var tickers = await sut.GetTickersAsync(Credentials());

        currencies["EUR"].AssetType.Should().Be("fiat");
        tickers.Data.Select(t => t.Symbol).Should().Contain("BTC/USD");
    }

    [Fact]
    public async Task GetPrivateTrades_SignsTheSortedEncodedQuery_OnTheDashedSymbolPath()
    {
        _handler.Respond("/api/1.0/trades/private/BTC-USD", RevolutXFixtures.TradesPage("next/1",
            RevolutXFixtures.Fill("0a1b", "BTC", "USD", "60000.10", "0.5", "buy", 1_700_000_000_500)));

        var response = await CreateSut().GetPrivateTradesAsync(
            Credentials(), "BTC-USD", 1_699_000_000_000, 1_700_000_000_000, "a b/c", 1000);

        var request = _handler.Requests.Should().ContainSingle().Which;
        const string query = "cursor=a%20b%2Fc&end_date=1700000000000&limit=1000&start_date=1699000000000";
        request.RequestUri!.PathAndQuery.Should().Be("/api/1.0/trades/private/BTC-USD?" + query);

        var signature = Convert.FromBase64String(request.Headers.GetValues(RevolutXSigner.SignatureHeader).Single());
        var message = RevolutXSigner.BuildMessage(
            RevolutXTestKeys.OpenSslBalancesTimestamp, "GET", "/api/1.0/trades/private/BTC-USD", query, string.Empty);
        var verifier = new Org.BouncyCastle.Crypto.Signers.Ed25519Signer();
        verifier.Init(false, new Org.BouncyCastle.Crypto.Parameters.Ed25519PublicKeyParameters(
            Convert.FromHexString(RevolutXTestKeys.Rfc8032Test1PublicKeyHex)));
        var bytes = System.Text.Encoding.UTF8.GetBytes(message);
        verifier.BlockUpdate(bytes, 0, bytes.Length);
        verifier.VerifySignature(signature).Should().BeTrue("the query is part of the signed payload");

        response.Metadata!.NextCursor.Should().Be("next/1");
        response.Data.Should().ContainSingle().Which.Should().BeEquivalentTo(new RevolutXTrade(
            "0a1b", "60000.10", "USD", "0.5", "BTC", 1_700_000_000_500, "o-0a1b", "buy"));
    }

    [Fact]
    public async Task GetPairs_ReadsTheSlashKeyedMap()
    {
        _handler.Respond("/api/1.0/configuration/pairs", RevolutXFixtures.Pairs);

        var pairs = await CreateSut().GetPairsAsync(Credentials());

        pairs["BTC/EUR"].Should().BeEquivalentTo(new RevolutXPair("BTC", "EUR", "active"));
    }

    [Fact]
    public async Task BaseUrl_IsConfigurable()
    {
        _handler.Respond("/api/1.0/balances", "[]");

        await CreateSut("http://revx.test/").GetBalancesAsync(Credentials());

        _handler.Requests.Single().RequestUri.Should().Be(new Uri("http://revx.test/api/1.0/balances"));
    }

    [Fact]
    public async Task RejectedKey_IsA422CarryingTheVenueMessage_AndCountsAsASticky()
    {
        _handler.Respond("/api/1.0/balances", RevolutXFixtures.Unauthorized, HttpStatusCode.Unauthorized);

        var act = () => CreateSut().GetBalancesAsync(Credentials());

        var thrown = (await act.Should().ThrowAsync<RevolutXException>()).Which;
        thrown.StatusCode.Should().Be(422);
        thrown.ErrorCode.Should().Be("INVALID_CREDENTIALS");
        thrown.VenueStatusCode.Should().Be(401);
        thrown.Message.Should().Contain("whitelisted IP");
        JobFailureTransientClassifier.IsTransient(thrown).Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ThrottledOrDown_IsClassifiedTransient(HttpStatusCode status)
    {
        _handler.Respond("/api/1.0/balances", "", status);

        var act = () => CreateSut().GetBalancesAsync(Credentials());

        var thrown = (await act.Should().ThrowAsync<RevolutXException>()).Which;
        thrown.VenueStatusCode.Should().Be((int)status);
        JobFailureTransientClassifier.IsTransient(thrown).Should().BeTrue();
    }

    [Fact]
    public async Task NetworkFailure_IsWrapped_AndTransient()
    {
        _handler.Throw = new HttpRequestException("connection refused", null, HttpStatusCode.ServiceUnavailable);

        var act = () => CreateSut().GetBalancesAsync(Credentials());

        var thrown = (await act.Should().ThrowAsync<RevolutXException>()).Which;
        thrown.Message.Should().Contain("unreachable");
        JobFailureTransientClassifier.IsTransient(thrown).Should().BeTrue();
    }

    [Fact]
    public async Task UnreadableBody_IsARevolutXFailure()
    {
        _handler.Respond("/api/1.0/balances", "<html>maintenance</html>");

        var act = () => CreateSut().GetBalancesAsync(Credentials());

        await act.Should().ThrowAsync<RevolutXException>().WithMessage("*unreadable*");
    }
}
