namespace FinanceSentry.Tests.Unit.BankSync.Monobank;

using System.Net;
using FinanceSentry.Modules.BankSync.Infrastructure.Monobank;
using FluentAssertions;
using Xunit;

/// <summary>A non-success Monobank response must keep its status and body (not EnsureSuccessStatusCode's).</summary>
public class MonobankErrorBodyTests
{
    [Fact]
    public async Task GetClientInfo_Http500_ThrowsMonobankExceptionWithStatusAndBody()
    {
        var client = new MonobankStubHttpHandler()
            .Enqueue(HttpStatusCode.InternalServerError, """{"errorDescription":"Unknown error"}""")
            .BuildClient();

        var ex = (await client.Invoking(c => c.GetClientInfoAsync("token"))
            .Should().ThrowAsync<MonobankException>()).Which;

        ex.StatusCode.Should().Be(500);
        ex.ErrorCode.Should().Be("MONOBANK_SERVER_ERROR");
        ex.Message.Should().Contain("500").And.Contain("Unknown error");
    }

    [Fact]
    public async Task GetClientInfo_Http400_KeepsTheReasonAndIsNotAServerError()
    {
        var client = new MonobankStubHttpHandler()
            .Enqueue(HttpStatusCode.BadRequest, """{"errorDescription":"Unknown account"}""")
            .BuildClient();

        var ex = (await client.Invoking(c => c.GetClientInfoAsync("token"))
            .Should().ThrowAsync<MonobankException>()).Which;

        ex.StatusCode.Should().Be(400);
        ex.ErrorCode.Should().Be("MONOBANK_HTTP_ERROR");
        ex.Message.Should().Contain("Unknown account");
    }
}
