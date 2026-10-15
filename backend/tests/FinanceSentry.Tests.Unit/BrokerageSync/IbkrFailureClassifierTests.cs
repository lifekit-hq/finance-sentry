using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using FinanceSentry.Core.Connections;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.OAuth;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Tests.Unit.BrokerageSync;

public class IbkrFailureClassifierTests
{
    private static readonly ProviderFailureContext Context =
        new(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero), new ConnectionHealth());

    public static TheoryData<Exception, FailureClass, FailureStrength, string> Cases => new()
    {
        // A bare 401/403 is only suspect: IBKR sends it for reasons other than a dead credential (report §5).
        { new BrokerAuthException("IBKR rejected the signed request to /v1/api/portfolio/accounts (401).", "IBKR", HttpStatusCode.Unauthorized), FailureClass.Credential, FailureStrength.Suspect, "HTTP_401" },
        { new BrokerAuthException("IBKR live-session-token request failed (403): {}", "IBKR", HttpStatusCode.Forbidden), FailureClass.Credential, FailureStrength.Suspect, "HTTP_403" },
        // During an outage IBKR answers the token request with 401 and "backend down".
        { new BrokerAuthException("IBKR live-session-token request failed (401): {\"error\":\"backend down\",\"statusCode\":401}", "IBKR", HttpStatusCode.Unauthorized), FailureClass.Transient, FailureStrength.Definitive, "HTTP_401" },
        { new BrokerAuthException("IBKR live-session-token request failed (503): Error 500 - Server Error", "IBKR", HttpStatusCode.ServiceUnavailable), FailureClass.Transient, FailureStrength.Definitive, "HTTP_503" },
        { new BrokerAuthException("IBKR live-session-token request failed (429): slow down", "IBKR", HttpStatusCode.TooManyRequests), FailureClass.RateLimited, FailureStrength.Definitive, "HTTP_429" },
        { new HttpRequestException("Bad Gateway", null, HttpStatusCode.BadGateway), FailureClass.Transient, FailureStrength.Definitive, nameof(HttpRequestException) },
        { new HttpRequestException("Connection refused", new SocketException()), FailureClass.Transient, FailureStrength.Definitive, nameof(HttpRequestException) },
        { new TaskCanceledException("The request timed out."), FailureClass.Transient, FailureStrength.Definitive, nameof(TaskCanceledException) },
        // IBKR answered, but not in a way that says whose fault it is.
        { new BrokerAuthException("IBKR OAuth session returned no accounts — the consumer key may not be active yet.", "IBKR"), FailureClass.Unknown, FailureStrength.Definitive, nameof(BrokerAuthException) },
        { new BrokerAuthException("IBKR live session token failed signature validation.", "IBKR"), FailureClass.Unknown, FailureStrength.Definitive, nameof(BrokerAuthException) },
        { new HttpRequestException("Not Found", null, HttpStatusCode.NotFound), FailureClass.Unknown, FailureStrength.Definitive, nameof(HttpRequestException) },
        // A stored credential we cannot read: only connecting again fixes it.
        { new BrokerCredentialUnreadableException("IBKR", new CryptographicException("The computed authentication tag did not match the input authentication tag.")), FailureClass.Credential, FailureStrength.Definitive, nameof(BrokerCredentialUnreadableException) },
        { new CryptographicException("Bad PKCS7 padding."), FailureClass.Credential, FailureStrength.Definitive, nameof(CryptographicException) },
        // Anything else unexpected counts as unknown.
        { new InvalidOperationException("No IBKR accounts found for the authenticated session."), FailureClass.Unknown, FailureStrength.Definitive, nameof(InvalidOperationException) },
        { new System.Text.Json.JsonException("unreadable ledger"), FailureClass.Unknown, FailureStrength.Definitive, "JsonException" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Classify_MapsEachIbkrFailureShape(Exception ex, FailureClass expectedClass, FailureStrength expectedStrength, string expectedCode)
    {
        var failure = new IbkrFailureClassifier().Classify(ex, Context);

        failure.Should().Be(new ProviderFailure(expectedClass, expectedStrength, expectedCode));
    }

    [Fact]
    public void Provider_IsTheSlugTheSyncJobAlertsUnder() =>
        new IbkrFailureClassifier().Provider.Should().Be("ibkr");
}
