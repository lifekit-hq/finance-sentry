using System.Reflection;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceSentry.Tests.Unit.BrokerageSync.Flex;

public class IbkrFlexClientTests
{
    private const string SendRequestSuccess =
        """<FlexStatementResponse><Status>Success</Status><ReferenceCode>REF123</ReferenceCode><Url>https://flex.test/GetStatement</Url></FlexStatementResponse>""";

    private const string NotYetGenerated =
        """<FlexStatementResponse><Status>Warn</Status><ErrorCode>1019</ErrorCode><ErrorMessage>Statement generation in progress. Please try again shortly.</ErrorMessage></FlexStatementResponse>""";

    private const string StatementNotAvailable =
        """<FlexStatementResponse><Status>Fail</Status><ErrorCode>1003</ErrorCode><ErrorMessage>Statement is not available.</ErrorMessage></FlexStatementResponse>""";

    private static readonly IbkrFlexCredentials Credentials = new(Guid.NewGuid(), "synthetic-token", "999999");

    private static string LoadSampleStatementXml()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith("sample-flex-statement.xml", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static IbkrFlexClient CreateClient(SequencedHttpStubHandler handler, IbkrFlexOptions? options = null)
    {
        var httpClient = new HttpClient(handler);
        var opts = options ?? new IbkrFlexOptions
        {
            PollInterval = TimeSpan.FromMilliseconds(10),
            SendRequestRetryDelay = TimeSpan.FromMilliseconds(1),
        };
        return new IbkrFlexClient(
            httpClient,
            Microsoft.Extensions.Options.Options.Create(opts),
            new IbkrFlexRateLimiter(maxPerWindow: 1000, window: TimeSpan.FromMinutes(1), minInterval: TimeSpan.Zero),
            NullLogger<IbkrFlexClient>.Instance);
    }

    [Fact]
    public async Task FetchStatementAsync_SendRequestThenReadyStatement_ReturnsParsedStatement()
    {
        var handler = new SequencedHttpStubHandler()
            .Enqueue("/AccountManagement/FlexWebService/SendRequest", SendRequestSuccess)
            .Enqueue("/AccountManagement/FlexWebService/GetStatement", LoadSampleStatementXml());
        var client = CreateClient(handler);

        var statement = await client.FetchStatementAsync(Credentials, ct: CancellationToken.None);

        statement.AccountId.Should().Be("U0000001");
        handler.RequestUris.Should().HaveCount(2);
        handler.RequestUris[0].AbsolutePath.Should().Be("/AccountManagement/FlexWebService/SendRequest");
        handler.RequestUris[1].AbsolutePath.Should().Be("/AccountManagement/FlexWebService/GetStatement");
        handler.RequestUris[1].Query.Should().Contain("q=REF123");
    }

    [Fact]
    public async Task FetchStatementAsync_NotYetGeneratedThenReady_RetriesUntilStatementIsReady()
    {
        var handler = new SequencedHttpStubHandler()
            .Enqueue("/AccountManagement/FlexWebService/SendRequest", SendRequestSuccess)
            .Enqueue("/AccountManagement/FlexWebService/GetStatement", NotYetGenerated)
            .Enqueue("/AccountManagement/FlexWebService/GetStatement", NotYetGenerated)
            .Enqueue("/AccountManagement/FlexWebService/GetStatement", LoadSampleStatementXml());
        var client = CreateClient(handler);

        var statement = await client.FetchStatementAsync(Credentials, ct: CancellationToken.None);

        statement.AccountId.Should().Be("U0000001");
        handler.RequestUris.Should().HaveCount(4);
    }

    [Fact]
    public async Task FetchStatementAsync_NeverReady_ThrowsAfterMaxPollAttempts()
    {
        var handler = new SequencedHttpStubHandler()
            .Enqueue("/AccountManagement/FlexWebService/SendRequest", SendRequestSuccess)
            .Enqueue("/AccountManagement/FlexWebService/GetStatement", NotYetGenerated);
        var client = CreateClient(handler, new IbkrFlexOptions
        {
            MaxPollAttempts = 3,
            PollInterval = TimeSpan.FromMilliseconds(1),
        });

        var act = () => client.FetchStatementAsync(Credentials, ct: CancellationToken.None);

        await act.Should().ThrowAsync<IbkrFlexException>();
        handler.RequestUris.Should().HaveCount(4); // SendRequest + 3 polls
    }

    [Fact]
    public async Task FetchStatementAsync_SendRequestError_ThrowsWithFlexErrorCode_WithoutRetrying()
    {
        const string error =
            """<FlexStatementResponse><Status>Fail</Status><ErrorCode>1015</ErrorCode><ErrorMessage>Token is invalid.</ErrorMessage></FlexStatementResponse>""";
        var handler = new SequencedHttpStubHandler().Enqueue("/AccountManagement/FlexWebService/SendRequest", error);
        var client = CreateClient(handler);

        var act = () => client.FetchStatementAsync(Credentials, ct: CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<IbkrFlexException>();
        thrown.Which.FlexErrorCode.Should().Be("1015");
        handler.RequestUris.Should().ContainSingle();
    }

    [Theory]
    [InlineData(StatementNotAvailable)]
    [InlineData(NotYetGenerated)]
    public async Task FetchStatementAsync_SendRequestNotAvailableOrInProgress_IsRetried_ThenSucceeds(string firstAnswer)
    {
        var handler = new SequencedHttpStubHandler()
            .Enqueue("/AccountManagement/FlexWebService/SendRequest", firstAnswer)
            .Enqueue("/AccountManagement/FlexWebService/SendRequest", SendRequestSuccess)
            .Enqueue("/AccountManagement/FlexWebService/GetStatement", LoadSampleStatementXml());
        var client = CreateClient(handler);

        var statement = await client.FetchStatementAsync(Credentials, ct: CancellationToken.None);

        statement.AccountId.Should().Be("U0000001");
        handler.RequestUris.Select(u => u.AbsolutePath).Should().Equal(
            "/AccountManagement/FlexWebService/SendRequest",
            "/AccountManagement/FlexWebService/SendRequest",
            "/AccountManagement/FlexWebService/GetStatement");
    }

    [Fact]
    public async Task FetchStatementAsync_SendRequestNeverAvailable_ThrowsTheFlexCode_AfterMaxAttempts()
    {
        var handler = new SequencedHttpStubHandler().Enqueue("/AccountManagement/FlexWebService/SendRequest", StatementNotAvailable);
        var client = CreateClient(handler, new IbkrFlexOptions
        {
            SendRequestMaxAttempts = 3,
            SendRequestRetryDelay = TimeSpan.FromMilliseconds(1),
        });

        var act = () => client.FetchStatementAsync(
            Credentials, new FlexStatementWindow(new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 13)), CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<IbkrFlexException>();
        thrown.Which.IsStatementNotAvailable.Should().BeTrue();
        handler.RequestUris.Should().HaveCount(3);
        handler.RequestUris.Should().OnlyContain(u => u.Query.Contains("fd=20261007") && u.Query.Contains("td=20261013"));
    }

    [Theory]
    [InlineData(2)]
    public async Task FetchStatementAsync_ParsesAllThreeSyntheticSections(int expectedTradeCount)
    {
        var handler = new SequencedHttpStubHandler()
            .Enqueue("/AccountManagement/FlexWebService/SendRequest", SendRequestSuccess)
            .Enqueue("/AccountManagement/FlexWebService/GetStatement", LoadSampleStatementXml());
        var client = CreateClient(handler);

        var statement = await client.FetchStatementAsync(Credentials, ct: CancellationToken.None);

        statement.Trades.Should().HaveCount(expectedTradeCount);
        statement.Trades[0].Symbol.Should().Be("ZZZQ");
        statement.Trades[0].TradeId.Should().Be("700000001");

        statement.CashTransactions.Should().HaveCount(2);
        statement.CashTransactions[0].Type.Should().Be("Dividends");
        statement.CashTransactions[0].Amount.Should().Be("1.00");

        statement.FinancialInstruments.Should().ContainSingle();
        statement.FinancialInstruments[0].Symbol.Should().Be("ZZZQ");
        statement.FinancialInstruments[0].Isin.Should().Be("US0000000001");
    }
}
