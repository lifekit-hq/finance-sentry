using System.Net;
using System.Net.Sockets;
using FinanceSentry.Core.Connections;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using FinanceSentry.Modules.CryptoSync.Infrastructure.RevolutX;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Tests.Unit.CryptoSync;

public class RevolutXFailureClassifierTests
{
    private static readonly ProviderFailureContext Context =
        new(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero), new ConnectionHealth());

    // The shape RevolutXHttpClient throws for a non-success answer.
    private static RevolutXException ApiError(HttpStatusCode status) =>
        new($"Revolut X API error (HTTP {(int)status}): detail", new HttpRequestException("detail", inner: null, status));

    public static TheoryData<Exception, FailureClass, FailureStrength, string> Cases => new()
    {
        // Revolut X sends no code that says the key itself is gone: a 401/403 is only suspect.
        { ApiError(HttpStatusCode.Unauthorized), FailureClass.Credential, FailureStrength.Suspect, "HTTP_401" },
        { ApiError(HttpStatusCode.Forbidden), FailureClass.Credential, FailureStrength.Suspect, "HTTP_403" },
        { new CryptoTradeHistoryException(CryptoExchangeProvider.RevolutX, ["BTC"], ApiError(HttpStatusCode.Unauthorized)), FailureClass.Credential, FailureStrength.Suspect, "HTTP_401" },
        // Throttling, 5xx, network, timeout.
        { ApiError(HttpStatusCode.ServiceUnavailable), FailureClass.Transient, FailureStrength.Definitive, "HTTP_503" },
        { ApiError(HttpStatusCode.TooManyRequests), FailureClass.RateLimited, FailureStrength.Definitive, "HTTP_429" },
        { new RevolutXException("Revolut X is unreachable.", new HttpRequestException("Connection refused", new SocketException())), FailureClass.Transient, FailureStrength.Definitive, nameof(RevolutXException) },
        { new RevolutXException("Revolut X did not respond in time.", new TaskCanceledException("timeout")), FailureClass.Transient, FailureStrength.Definitive, nameof(RevolutXException) },
        { new CryptoTradeHistoryException(CryptoExchangeProvider.RevolutX, ["ETH"], ApiError(HttpStatusCode.BadGateway)), FailureClass.Transient, FailureStrength.Definitive, "HTTP_502" },
        // Revolut X answered, but not in a way that says whose fault it is.
        { ApiError(HttpStatusCode.BadRequest), FailureClass.Unknown, FailureStrength.Definitive, "HTTP_400" },
        { ApiError(HttpStatusCode.NotFound), FailureClass.Unknown, FailureStrength.Definitive, "HTTP_404" },
        // A key pair that cannot be loaded, or a stored key we cannot decrypt: only entering it again fixes it.
        { RevolutXException.UnreadableKey("The Revolut X private key could not be read as a PEM Ed25519 key."), FailureClass.Credential, FailureStrength.Definitive, nameof(RevolutXException) },
        { new ExchangeCredentialUnreadableException(CryptoExchangeProvider.RevolutX, new System.Security.Cryptography.CryptographicException("tag mismatch")), FailureClass.Credential, FailureStrength.Definitive, nameof(ExchangeCredentialUnreadableException) },
        // Anything else unexpected counts as unknown.
        { new RevolutXException("Revolut X returned an unreadable response for /balances.", new System.Text.Json.JsonException("bad")), FailureClass.Unknown, FailureStrength.Definitive, nameof(RevolutXException) },
        { new RevolutXException("Revolut X returned more than 50 pages of BTC-USD fills for one window."), FailureClass.Unknown, FailureStrength.Definitive, nameof(RevolutXException) },
        { new InvalidOperationException("Sequence contains no elements"), FailureClass.Unknown, FailureStrength.Definitive, nameof(InvalidOperationException) },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Classify_MapsEachRevolutXFailureShape(Exception ex, FailureClass expectedClass, FailureStrength expectedStrength, string expectedCode)
    {
        var failure = new RevolutXFailureClassifier().Classify(ex, Context);

        failure.Should().Be(new ProviderFailure(expectedClass, expectedStrength, expectedCode));
    }

    [Fact]
    public void Provider_IsTheVenueSlug() =>
        new RevolutXFailureClassifier().Provider.Should().Be(CryptoExchangeProvider.RevolutX);
}
