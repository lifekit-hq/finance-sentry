namespace FinanceSentry.Tests.Unit.BrokerageSync.Flex;

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using FinanceSentry.Core.Connections;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

public class IbkrFlexFailureClassifierTests
{
    private static readonly ProviderFailureContext Context =
        new(new DateTimeOffset(2026, 10, 1, 6, 0, 0, TimeSpan.Zero), new ConnectionHealth());

    public static TheoryData<Exception, FailureClass, FailureStrength, string> Cases => new()
    {
        // IBKR says plainly that only the user can fix the connection.
        { new IbkrFlexException("1012", "Token has expired."), FailureClass.Credential, FailureStrength.Definitive, "1012" },
        { new IbkrFlexException("1013", "IP restriction."), FailureClass.Credential, FailureStrength.Definitive, "1013" },
        { new IbkrFlexException("1014", "Query is invalid."), FailureClass.Credential, FailureStrength.Definitive, "1014" },
        { new IbkrFlexException("1015", "Token is invalid."), FailureClass.Credential, FailureStrength.Definitive, "1015" },
        // Ambiguous refusals: suspect until seen again.
        { new IbkrFlexException("1020", "Invalid request or unable to validate request."), FailureClass.Credential, FailureStrength.Suspect, "1020" },
        { new IbkrFlexException(null, "IBKR Flex rejected the request (403).", HttpStatusCode.Forbidden), FailureClass.Credential, FailureStrength.Suspect, "HTTP_403" },
        // Not ready yet, under load or throttled.
        { new IbkrFlexException("1001", "Statement could not be generated at this time. Please try again shortly."), FailureClass.Transient, FailureStrength.Definitive, "1001" },
        { new IbkrFlexException("1009", "The server is under heavy load."), FailureClass.Transient, FailureStrength.Definitive, "1009" },
        { new IbkrFlexException("1018", "Too many requests have been made from this token."), FailureClass.Transient, FailureStrength.Definitive, "1018" },
        { new IbkrFlexException("1019", "Statement for reference 123 was not ready after 5 polls."), FailureClass.Transient, FailureStrength.Definitive, "1019" },
        { new HttpRequestException("Service Unavailable", null, HttpStatusCode.ServiceUnavailable), FailureClass.Transient, FailureStrength.Definitive, nameof(HttpRequestException) },
        { new HttpRequestException("Connection refused", new SocketException()), FailureClass.Transient, FailureStrength.Definitive, nameof(HttpRequestException) },
        // "Statement is not available", once the client's retries and the job's step-back are spent: unknown.
        { new IbkrFlexException("1003", "Statement is not available."), FailureClass.Unknown, FailureStrength.Definitive, "1003" },
        // A stored token we cannot read: only entering it again fixes it.
        { new BrokerCredentialUnreadableException("IBKR Flex", new CryptographicException("tag mismatch")), FailureClass.Credential, FailureStrength.Definitive, nameof(BrokerCredentialUnreadableException) },
        // IBKR answered with something we cannot place, or anything else unexpected: unknown.
        { new IbkrFlexException("1010", "Legacy Flex Queries are no longer supported."), FailureClass.Unknown, FailureStrength.Definitive, "1010" },
        { new IbkrFlexException(null, "IBKR Flex returned an empty statement."), FailureClass.Unknown, FailureStrength.Definitive, nameof(IbkrFlexException) },
        { new InvalidOperationException("There is an error in XML document (1, 1)."), FailureClass.Unknown, FailureStrength.Definitive, nameof(InvalidOperationException) },
        { new DbUpdateException("An error occurred while saving the entity changes."), FailureClass.Unknown, FailureStrength.Definitive, nameof(DbUpdateException) },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Classify_MapsEachFlexFailureShape(Exception ex, FailureClass expectedClass, FailureStrength expectedStrength, string expectedCode)
    {
        var failure = new IbkrFlexFailureClassifier().Classify(ex, Context);

        failure.Should().Be(new ProviderFailure(expectedClass, expectedStrength, expectedCode));
    }

    [Fact]
    public void Provider_IsTheSlugTheSyncJobAlertsUnder() =>
        new IbkrFlexFailureClassifier().Provider.Should().Be("ibkr-flex");

    [Theory]
    [InlineData("1003", true)]
    [InlineData("1019", false)]
    [InlineData(null, false)]
    public void StatementNotAvailable_IsOnlyIbkrsNotAvailableCode(string? flexCode, bool expected) =>
        new IbkrFlexException(flexCode, "msg").IsStatementNotAvailable.Should().Be(expected);
}
