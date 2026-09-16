using System.Net;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
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
    public async Task GetHoldings_ReturnsPricedCryptoHoldings()
    {
        var holdings = await CreateSut().GetHoldingsAsync(ApiKey, RevolutXTestKeys.Ed25519PrivateKeyPem);

        holdings.Select(h => h.Asset).Should().Equal("BTC", "ETH", "DOGE", "USDC");
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

    [Fact]
    public async Task GetTrades_ReportsNothing_AndKeepsTheCursor_UntilIngestionLands()
    {
        var page = await CreateSut().GetTradesAsync(ApiKey, RevolutXTestKeys.Ed25519PrivateKeyPem, "BTC", "cursor-1");

        page.Trades.Should().BeEmpty();
        page.NextCursor.Should().Be("cursor-1");
        _handler.Requests.Should().BeEmpty();
    }
}
