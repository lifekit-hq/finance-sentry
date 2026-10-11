namespace FinanceSentry.Tests.Unit.Http;

using System.Net;
using FinanceSentry.Infrastructure.Http;
using FluentAssertions;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Xunit;

public class TransientHttpStatusTests
{
    private const int FirstStatus = 100;
    private const int LastStatus = 599;

    [Fact]
    public void IsTransient_AgreesWithThePlatformPredicateForEveryStatus()
    {
        for (var code = FirstStatus; code <= LastStatus; code++)
        {
            using var response = new HttpResponseMessage((HttpStatusCode)code);
            var platform = HttpClientResiliencePredicates.IsTransient(Outcome.FromResult(response));

            TransientHttpStatus.IsTransient((HttpStatusCode)code).Should().Be(platform, $"status {code}");
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public void IsTransient_RetryableStatuses(HttpStatusCode status) =>
        TransientHttpStatus.IsTransient(status).Should().BeTrue();

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public void IsTransient_PermanentStatuses(HttpStatusCode status) =>
        TransientHttpStatus.IsTransient(status).Should().BeFalse();
}
