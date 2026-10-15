namespace FinanceSentry.Tests.Unit.Http;

using System.Net;
using System.Net.Http.Headers;
using FinanceSentry.Infrastructure.Http;
using FluentAssertions;
using Xunit;

public class RetryAfterHeaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Seconds_AreTheWait()
    {
        using var response = new HttpResponseMessage((HttpStatusCode)429);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(60));

        RetryAfterHeader.WaitFrom(response, Now).Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void ADate_IsTheWaitFromNow()
    {
        using var response = new HttpResponseMessage((HttpStatusCode)429);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(Now.AddMinutes(3));

        RetryAfterHeader.WaitFrom(response, Now).Should().Be(TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ADateThatHasPassed_IsNoWait()
    {
        using var response = new HttpResponseMessage((HttpStatusCode)429);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(Now.AddMinutes(-3));

        RetryAfterHeader.WaitFrom(response, Now).Should().BeNull();
    }

    [Fact]
    public void NoHeader_IsNoWait()
    {
        using var response = new HttpResponseMessage((HttpStatusCode)429);

        RetryAfterHeader.WaitFrom(response, Now).Should().BeNull();
    }

    [Fact]
    public void ZeroSeconds_IsNoWait()
    {
        RetryAfterHeader.WaitFrom(new RetryConditionHeaderValue(TimeSpan.Zero), Now).Should().BeNull();
    }
}
