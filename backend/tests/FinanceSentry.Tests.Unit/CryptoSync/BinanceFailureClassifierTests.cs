using System.Net.Sockets;
using FinanceSentry.Core.Connections;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Binance;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Tests.Unit.CryptoSync;

public class BinanceFailureClassifierTests
{
    private static readonly ProviderFailureContext Context =
        new(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero), new ConnectionHealth());

    public static TheoryData<Exception, FailureClass, FailureStrength, string> Cases => new()
    {
        // Binance's invalid-key codes say plainly the key is not honoured.
        { new BinanceException("Invalid API-key, IP, or permissions for action.", binanceErrorCode: -2015, venueStatusCode: 401), FailureClass.Credential, FailureStrength.Definitive, "-2015" },
        { new BinanceException("API-key format invalid.", binanceErrorCode: -2014, venueStatusCode: 401), FailureClass.Credential, FailureStrength.Definitive, "-2014" },
        { new BinanceException("Signature for this request is not valid.", binanceErrorCode: -1022, venueStatusCode: 400), FailureClass.Credential, FailureStrength.Definitive, "-1022" },
        { new CryptoTradeHistoryException(CryptoExchangeProvider.Binance, ["BTC"], new BinanceException("Invalid API-key", binanceErrorCode: -2015, venueStatusCode: 401)), FailureClass.Credential, FailureStrength.Definitive, "-2015" },
        // A bare 401/403 is only suspect.
        { new BinanceException("Binance API error: HTTP 401", venueStatusCode: 401), FailureClass.Credential, FailureStrength.Suspect, "HTTP_401" },
        { new BinanceException("Binance API error: HTTP 403", venueStatusCode: 403), FailureClass.Credential, FailureStrength.Suspect, "HTTP_403" },
        // Throttling, 5xx, network.
        { new BinanceException("Binance API error: HTTP 500", venueStatusCode: 500), FailureClass.Transient, FailureStrength.Definitive, "HTTP_500" },
        { new BinanceException("Too many requests.", binanceErrorCode: -1003, venueStatusCode: 429), FailureClass.RateLimited, FailureStrength.Definitive, "-1003" },
        { new CryptoTradeHistoryException(CryptoExchangeProvider.Binance, ["BTC"], new BinanceException("HTTP 429", venueStatusCode: 429)), FailureClass.RateLimited, FailureStrength.Definitive, "HTTP_429" },
        { new HttpRequestException("Connection refused", new SocketException()), FailureClass.Transient, FailureStrength.Definitive, nameof(HttpRequestException) },
        { new TaskCanceledException("The request timed out."), FailureClass.Transient, FailureStrength.Definitive, nameof(TaskCanceledException) },
        // Binance answered, but not in a way that says whose fault it is.
        { new BinanceException("Timestamp for this request is outside of the recvWindow.", binanceErrorCode: -1021, venueStatusCode: 400), FailureClass.Unknown, FailureStrength.Definitive, "-1021" },
        { new BinanceException("Binance API error: HTTP 418", venueStatusCode: 418), FailureClass.Unknown, FailureStrength.Definitive, "HTTP_418" },
        { new BinanceException("Binance API returned empty response."), FailureClass.Unknown, FailureStrength.Definitive, nameof(BinanceException) },
        // A stored key we cannot decrypt: only entering it again fixes it.
        { new ExchangeCredentialUnreadableException(CryptoExchangeProvider.Binance, new System.Security.Cryptography.CryptographicException("tag mismatch")), FailureClass.Credential, FailureStrength.Definitive, nameof(ExchangeCredentialUnreadableException) },
        // Anything else unexpected counts as unknown.
        { new System.Text.Json.JsonException("unreadable account payload"), FailureClass.Unknown, FailureStrength.Definitive, "JsonException" },
        { new InvalidOperationException("Sequence contains no elements"), FailureClass.Unknown, FailureStrength.Definitive, nameof(InvalidOperationException) },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Classify_MapsEachBinanceFailureShape(Exception ex, FailureClass expectedClass, FailureStrength expectedStrength, string expectedCode)
    {
        var failure = new BinanceFailureClassifier().Classify(ex, Context);

        failure.Should().Be(new ProviderFailure(expectedClass, expectedStrength, expectedCode));
    }

    [Fact]
    public void Provider_IsTheVenueSlug() =>
        new BinanceFailureClassifier().Provider.Should().Be(CryptoExchangeProvider.Binance);
}
