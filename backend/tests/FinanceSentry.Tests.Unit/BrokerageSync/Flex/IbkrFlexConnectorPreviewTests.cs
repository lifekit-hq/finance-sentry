using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BrokerageSync.Application.Connect;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinanceSentry.Tests.Unit.BrokerageSync.Flex;

public class IbkrFlexConnectorPreviewTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private readonly Mock<IIbkrFlexClient> _flexClient = new(MockBehavior.Strict);

    private readonly Mock<IIBKRFlexCredentialRepository> _credentials = new(MockBehavior.Strict);
    private readonly Mock<ICredentialEncryptionService> _encryption = new(MockBehavior.Strict);
    private readonly Mock<IBackgroundJobClient> _jobs = new(MockBehavior.Strict);

    private IbkrFlexConnector CreateConnector() => new(
        _credentials.Object,
        _encryption.Object,
        _flexClient.Object,
        _jobs.Object,
        NullLogger<IbkrFlexConnector>.Instance);

    [Fact]
    public async Task ConnectAsync_EnqueuesOneOffFlexSyncForTheConnectingUser()
    {
        _encryption.Setup(e => e.Encrypt("tok")).Returns(new EncryptionResult([1], [2], [3], 1));
        _credentials.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IBKRFlexCredential?)null);
        _credentials.Setup(r => r.AddAsync(It.IsAny<IBKRFlexCredential>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _credentials.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        Job? enqueued = null;
        _jobs.Setup(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()))
            .Callback<Job, IState>((job, _) => enqueued = job)
            .Returns("1");

        await CreateConnector().ConnectAsync(UserId, new ConnectIbkrFlexArtifacts("tok", "123456"), CancellationToken.None);

        enqueued.Should().NotBeNull();
        enqueued!.Type.Should().Be(typeof(IIbkrFlexTradeSyncService));
        enqueued.Method.Name.Should().Be(nameof(IIbkrFlexTradeSyncService.SyncAsync));
        enqueued.Args[0].Should().Be(UserId);
        enqueued.Args[1].Should().BeNull();
    }

    [Fact]
    public async Task PreviewAsync_SummarisesStatement_AndPersistsNothing()
    {
        var statement = new FlexStatementXml
        {
            AccountId = "U1234567",
            FromDate = "20250101",
            ToDate = "20251231",
            WhenGenerated = "20260105;143015",
            Trades = [new FlexTradeXml(), new FlexTradeXml()],
            CashTransactions = [new FlexCashTransactionXml()],
            OpenPositions =
            [
                new FlexOpenPositionXml { Symbol = "VOO", LevelOfDetail = "SUMMARY" },
                new FlexOpenPositionXml { Symbol = "VOO", LevelOfDetail = "LOT" },
                new FlexOpenPositionXml { Symbol = "AAPL", LevelOfDetail = "SUMMARY" },
            ],
            CashReport =
            [
                new FlexCashReportCurrencyXml { Currency = "USD", LevelOfDetail = "Currency" },
                new FlexCashReportCurrencyXml { Currency = "EUR", LevelOfDetail = "Currency" },
                new FlexCashReportCurrencyXml { Currency = "BASE_SUMMARY", LevelOfDetail = "BASE_SUMMARY" },
            ],
        };
        _flexClient
            .Setup(c => c.FetchStatementAsync(
                It.Is<IbkrFlexCredentials>(x => x.Token == "tok" && x.QueryId == "123456"),
                null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(statement);

        var preview = await CreateConnector().PreviewAsync(
            UserId, new ConnectIbkrFlexArtifacts("tok", "123456"), CancellationToken.None);

        preview.AccountId.Should().Be("U1234567");
        preview.FromDate.Should().Be(new DateOnly(2025, 1, 1));
        preview.ToDate.Should().Be(new DateOnly(2025, 12, 31));
        preview.GeneratedAtUtc.Should().Be(new DateTime(2026, 1, 5, 14, 30, 15, DateTimeKind.Utc));
        preview.OpenPositionsCount.Should().Be(2);
        preview.CashCurrencies.Should().Equal("EUR", "USD");
        preview.TradesCount.Should().Be(2);
        preview.CashTransactionsCount.Should().Be(1);
    }

    [Fact]
    public async Task PreviewAsync_UnparseableDates_AreNull()
    {
        _flexClient
            .Setup(c => c.FetchStatementAsync(It.IsAny<IbkrFlexCredentials>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FlexStatementXml { AccountId = "U1", FromDate = "", ToDate = "x" });

        var preview = await CreateConnector().PreviewAsync(
            UserId, new ConnectIbkrFlexArtifacts("t", "1"), CancellationToken.None);

        preview.FromDate.Should().BeNull();
        preview.ToDate.Should().BeNull();
        preview.GeneratedAtUtc.Should().BeNull();
        preview.OpenPositionsCount.Should().Be(0);
        preview.CashCurrencies.Should().BeEmpty();
    }

    [Fact]
    public async Task PreviewAsync_IbkrRejection_PropagatesTheException()
    {
        _flexClient
            .Setup(c => c.FetchStatementAsync(It.IsAny<IbkrFlexCredentials>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IbkrFlexException("1015", "Invalid token"));

        var act = () => CreateConnector().PreviewAsync(
            UserId, new ConnectIbkrFlexArtifacts("bad", "1"), CancellationToken.None);

        (await act.Should().ThrowAsync<IbkrFlexException>()).Which.ErrorCode.Should().Be("IBKR_FLEX_INVALID_TOKEN");
    }

    [Theory]
    [InlineData("1015", "IBKR_FLEX_INVALID_TOKEN")]
    [InlineData("1012", "IBKR_FLEX_TOKEN_EXPIRED")]
    [InlineData("1013", "IBKR_FLEX_IP_RESTRICTED")]
    [InlineData("1014", "IBKR_FLEX_QUERY_NOT_FOUND")]
    [InlineData("1020", "IBKR_FLEX_QUERY_NOT_FOUND")]
    [InlineData("1019", "IBKR_FLEX_NOT_READY")]
    [InlineData("1009", "IBKR_FLEX_NOT_READY")]
    [InlineData("1018", "IBKR_FLEX_RATE_LIMITED")]
    [InlineData("1003", "IBKR_FLEX_ERROR")]
    [InlineData(null, "IBKR_FLEX_ERROR")]
    public void IbkrFlexException_MapsFlexCodeToActionableApiCode(string? flexCode, string expected)
    {
        var ex = new IbkrFlexException(flexCode, "msg");

        ex.ErrorCode.Should().Be(expected);
        ex.StatusCode.Should().Be(422);
    }
}
