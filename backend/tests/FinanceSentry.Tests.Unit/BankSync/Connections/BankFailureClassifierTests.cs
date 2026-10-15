namespace FinanceSentry.Tests.Unit.BankSync.Connections;

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using FinanceSentry.Core.Connections;
using FinanceSentry.Modules.BankSync.Application.Connections;
using FinanceSentry.Modules.BankSync.Infrastructure.Monobank;
using FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// The Monobank and TrueLayer failure classifiers (connection health, Option B, S2) against each provider's
/// real failure shapes, as their HTTP clients throw them: credential loss (definitive or suspect, including a stored
/// credential we cannot decrypt), transient, resource gone, unknown, and the parse errors that say nothing about the
/// connection (internal).
/// </summary>
public class BankFailureClassifierTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
    private static readonly ProviderFailureContext Context = new(At, new ConnectionHealth());

    public static TheoryData<Exception, FailureClass, FailureStrength, string?> MonobankShapes => new()
    {
        // MonobankHttpClient: 401 and 403 both become MONOBANK_TOKEN_INVALID; Monobank sends 403 for more than a dead token.
        { new MonobankException("MONOBANK_TOKEN_INVALID", "Invalid or expired Monobank token.", 400), FailureClass.Credential, FailureStrength.Suspect, "MONOBANK_TOKEN_INVALID" },
        { new MonobankException("MONOBANK_RATE_LIMITED", "Monobank API rate limit exceeded.", 429), FailureClass.RateLimited, FailureStrength.Definitive, "MONOBANK_RATE_LIMITED" },
        { new MonobankException("MONOBANK_SERVER_ERROR", "Monobank API error (502): bad gateway", 502), FailureClass.Transient, FailureStrength.Definitive, "MONOBANK_SERVER_ERROR" },
        { new MonobankException("MONOBANK_HTTP_ERROR", "Monobank API error (400): {\"errorDescription\":\"Unknown account\"}", 400), FailureClass.Unknown, FailureStrength.Definitive, "MONOBANK_HTTP_ERROR" },
        { new MonobankException("MONOBANK_PARSE_ERROR", "Failed to parse client info response."), FailureClass.Internal, FailureStrength.Definitive, "MONOBANK_PARSE_ERROR" },
        { new HttpRequestException("Connection refused", new SocketException()), FailureClass.Transient, FailureStrength.Definitive, MonobankFailureClassifier.UnavailableCode },
        { new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"), FailureClass.Transient, FailureStrength.Definitive, MonobankFailureClassifier.UnavailableCode },
        { new HttpRequestException("The SSL connection could not be established"), FailureClass.Unknown, FailureStrength.Definitive, null },
        // Ours, but a persistent failure of ours must still be told: counted, cause unknown.
        { new InvalidOperationException("Monobank credential not found."), FailureClass.Unknown, FailureStrength.Definitive, null },
        { new DbUpdateException("duplicate key"), FailureClass.Unknown, FailureStrength.Definitive, null },
        // A stored token we cannot decrypt: only the user entering it again restores the connection.
        { new CredentialUnreadableException(new CryptographicException("tag mismatch")), FailureClass.Credential, FailureStrength.Definitive, CredentialUnreadableException.Code },
    };

    [Theory]
    [MemberData(nameof(MonobankShapes))]
    public void Monobank_ReadsEachFailureShape(Exception exception, FailureClass expectedClass, FailureStrength expectedStrength, string? expectedCode)
    {
        var failure = new MonobankFailureClassifier().Classify(exception, Context);

        failure.Should().Be(new ProviderFailure(expectedClass, expectedStrength, expectedCode));
    }

    public static TheoryData<Exception, FailureClass, FailureStrength, string?> TrueLayerShapes => new()
    {
        // TrueLayerHttpClient: a dead consent is invalid_grant in the token endpoint's 400 body.
        { new TrueLayerException("TRUELAYER_BAD_REQUEST", "TrueLayer token endpoint error (400): {\"error\":\"invalid_grant\"}", 400), FailureClass.Credential, FailureStrength.Definitive, TrueLayerFailureClassifier.ConsentLostCode },
        { new TrueLayerException("TRUELAYER_FORBIDDEN", "TrueLayer API error (403): the consent has expired", 403), FailureClass.Credential, FailureStrength.Definitive, TrueLayerFailureClassifier.ConsentLostCode },
        { new TrueLayerException("TRUELAYER_UNAUTHORIZED", "TrueLayer API error (401): access has been revoked", 401), FailureClass.Credential, FailureStrength.Definitive, TrueLayerFailureClassifier.ConsentLostCode },
        { new TrueLayerException("TRUELAYER_UNAUTHORIZED", "TrueLayer API error (401) on /data/v1/accounts", 401), FailureClass.Credential, FailureStrength.Suspect, "TRUELAYER_UNAUTHORIZED" },
        { new TrueLayerException("TRUELAYER_FORBIDDEN", "TrueLayer API error (403) on /data/v1/accounts", 403), FailureClass.Credential, FailureStrength.Suspect, "TRUELAYER_FORBIDDEN" },
        { new TrueLayerException("TRUELAYER_RATE_LIMITED", "TrueLayer API error (429)", 429), FailureClass.RateLimited, FailureStrength.Definitive, "TRUELAYER_RATE_LIMITED" },
        { new TrueLayerException("TRUELAYER_ERROR", "TrueLayer API error (503)", 503), FailureClass.Transient, FailureStrength.Definitive, "TRUELAYER_ERROR" },
        { new TrueLayerException("TRUELAYER_NOT_FOUND", "TrueLayer API error (404) on /data/v1/accounts/x/transactions", 404), FailureClass.ResourceGone, FailureStrength.Definitive, "TRUELAYER_NOT_FOUND" },
        { new TrueLayerException("TRUELAYER_BAD_REQUEST", "TrueLayer API error (400): {\"error\":\"invalid_request\"}", 400), FailureClass.Unknown, FailureStrength.Definitive, "TRUELAYER_BAD_REQUEST" },
        { new TrueLayerException("TRUELAYER_PARSE_ERROR", "Empty body from /data/v1/accounts"), FailureClass.Internal, FailureStrength.Definitive, "TRUELAYER_PARSE_ERROR" },
        { new TrueLayerException("TRUELAYER_NOT_CONFIGURED", "TrueLayer credentials are not configured.", 503), FailureClass.Unknown, FailureStrength.Definitive, "TRUELAYER_NOT_CONFIGURED" },
        { new HttpRequestException("Service unavailable", null, HttpStatusCode.ServiceUnavailable), FailureClass.Transient, FailureStrength.Definitive, TrueLayerFailureClassifier.UnavailableCode },
        { new InvalidOperationException("TrueLayer connection not found."), FailureClass.Unknown, FailureStrength.Definitive, null },
        { new CredentialUnreadableException(new InvalidOperationException("Encryption key version 3 is not configured.")), FailureClass.Credential, FailureStrength.Definitive, CredentialUnreadableException.Code },
    };

    [Theory]
    [MemberData(nameof(TrueLayerShapes))]
    public void TrueLayer_ReadsEachFailureShape(Exception exception, FailureClass expectedClass, FailureStrength expectedStrength, string? expectedCode)
    {
        var failure = new TrueLayerFailureClassifier().Classify(exception, Context);

        failure.Should().Be(new ProviderFailure(expectedClass, expectedStrength, expectedCode));
    }

    [Theory]
    [InlineData(401, "TRUELAYER_UNAUTHORIZED")]
    [InlineData(403, "TRUELAYER_FORBIDDEN")]
    public void TrueLayer_RefusalOnceTheConsentHasExpired_IsADefinitiveCredentialLoss(int status, string code)
    {
        var refused = new TrueLayerException(code, $"TrueLayer API error ({status})", status);

        var failure = new TrueLayerFailureClassifier().Classify(refused, Context, consentExpiresAt: At);

        failure.Should().Be(ProviderFailure.CredentialDefinitive(code));
    }

    [Fact]
    public void Monobank_RateLimitWithRetryAfter_NamesWhenTheNextCallMayGo()
    {
        var limited = new MonobankException("MONOBANK_RATE_LIMITED", "Monobank API rate limit exceeded.", 429)
        {
            RetryAfter = TimeSpan.FromSeconds(60),
        };

        var failure = new MonobankFailureClassifier().Classify(limited, Context);

        failure.Should().Be(ProviderFailure.RateLimited("MONOBANK_RATE_LIMITED", At.AddSeconds(60)));
    }

    [Fact]
    public void TrueLayer_RateLimitWithRetryAfter_NamesWhenTheNextCallMayGo()
    {
        var limited = new TrueLayerException("TRUELAYER_RATE_LIMITED", "TrueLayer API error (429)", 429)
        {
            RetryAfter = TimeSpan.FromMinutes(5),
        };

        var failure = new TrueLayerFailureClassifier().Classify(limited, Context);

        failure.Should().Be(ProviderFailure.RateLimited("TRUELAYER_RATE_LIMITED", At.AddMinutes(5)));
    }

    [Fact]
    public void TrueLayer_ServerErrorWithRetryAfter_StaysTransientWithNoPause()
    {
        var down = new TrueLayerException("TRUELAYER_ERROR", "TrueLayer API error (503)", 503)
        {
            RetryAfter = TimeSpan.FromMinutes(5),
        };

        var failure = new TrueLayerFailureClassifier().Classify(down, Context);

        failure.Should().Be(ProviderFailure.Transient("TRUELAYER_ERROR"));
    }

    [Fact]
    public void TrueLayer_RefusalBeforeTheConsentExpires_StaysSuspect()
    {
        var refused = new TrueLayerException("TRUELAYER_FORBIDDEN", "TrueLayer API error (403)", 403);

        var failure = new TrueLayerFailureClassifier().Classify(refused, Context, consentExpiresAt: At.AddSeconds(1));

        failure.Should().Be(ProviderFailure.CredentialSuspect("TRUELAYER_FORBIDDEN"));
    }

    [Fact]
    public void Classifiers_NameTheProviderTheSyncPathsUse()
    {
        new MonobankFailureClassifier().Provider.Should().Be("monobank");
        new TrueLayerFailureClassifier().Provider.Should().Be("truelayer");
    }
}
