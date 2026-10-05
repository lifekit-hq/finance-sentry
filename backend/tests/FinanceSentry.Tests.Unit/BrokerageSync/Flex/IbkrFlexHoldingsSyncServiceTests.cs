namespace FinanceSentry.Tests.Unit.BrokerageSync.Flex;

using System.Globalization;
using System.Xml.Serialization;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// A Flex-only user gets positions and cash from the statement's Open Positions and Cash Report
/// sections; a healthy live OAuth credential takes precedence. All data below is synthetic.
/// </summary>
public class IbkrFlexHoldingsSyncServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static string Xml(string toDate) => $$"""
        <FlexQueryResponse queryName="SyntheticQuery" type="AF">
          <FlexStatements count="1">
            <FlexStatement accountId="U0000001" fromDate="{{toDate}}" toDate="{{toDate}}" period="LastBusinessDay">
              <OpenPositions>
                <OpenPosition accountId="U0000001" currency="USD" assetCategory="STK" symbol="ZZZQ" conid="900000001" isin="US0000000001" position="10" markPrice="50" positionValue="500" costBasisPrice="40" levelOfDetail="SUMMARY" />
                <OpenPosition accountId="U0000001" currency="USD" assetCategory="STK" symbol="ZZZQ" conid="900000001" position="4" positionValue="200" levelOfDetail="LOT" />
                <OpenPosition accountId="U0000001" currency="USD" assetCategory="STK" symbol="YYYQ" conid="900000002" position="0" positionValue="0" levelOfDetail="SUMMARY" />
              </OpenPositions>
              <CashReport>
                <CashReportCurrency accountId="U0000001" currency="USD" endingCash="125.5" levelOfDetail="Currency" />
                <CashReportCurrency accountId="U0000001" currency="BASE_SUMMARY" endingCash="125.5" levelOfDetail="BaseCurrency" />
              </CashReport>
            </FlexStatement>
          </FlexStatements>
        </FlexQueryResponse>
        """;

    private static FlexStatementXml Parse(string xml)
    {
        using var reader = new StringReader(xml);
        var response = (FlexQueryResponseXml)new XmlSerializer(typeof(FlexQueryResponseXml)).Deserialize(reader)!;
        return response.FlexStatements.Items[0];
    }

    private static string Today(int offsetDays = 0) =>
        DateTime.UtcNow.AddDays(offsetDays).ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private sealed class Rig
    {
        public readonly List<BrokerageHolding> Persisted = [];
        public readonly Mock<IIBKRCredentialRepository> Oauth = new();
        public readonly IbkrFlexHoldingsSyncService Service;

        public Rig()
        {
            var holdings = new Mock<IBrokerageHoldingRepository>();
            holdings.Setup(h => h.UpsertRangeAsync(It.IsAny<IEnumerable<BrokerageHolding>>(), It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<BrokerageHolding>, CancellationToken>((hs, _) => Persisted.AddRange(hs))
                .Returns(Task.CompletedTask);
            holdings.Setup(h => h.GetByUserIdUnscopedAsync(UserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Persisted.ToList());
            holdings.Setup(h => h.RemoveRange(It.IsAny<IEnumerable<BrokerageHolding>>()))
                .Callback<IEnumerable<BrokerageHolding>>(rs => Persisted.RemoveAll(rs.Contains));

            var instruments = new Mock<IBrokerageInstrumentRepository>();
            Service = new IbkrFlexHoldingsSyncService(
                Oauth.Object, holdings.Object, instruments.Object, NullLogger<IbkrFlexHoldingsSyncService>.Instance);
        }
    }

    [Fact]
    public async Task ApplyAsync_FlexOnly_PublishesSummaryPositionsAndCash_WithAsOfDate()
    {
        var rig = new Rig();

        var count = await rig.Service.ApplyAsync(UserId, Parse(Xml(Today())));

        count.Should().Be(2);
        rig.Persisted.Select(h => h.Symbol).Should().BeEquivalentTo("ZZZQ", "USD Cash");
        var stock = rig.Persisted.Single(h => h.Symbol == "ZZZQ");
        stock.Quantity.Should().Be(10m);
        stock.UsdValue.Should().Be(500m);
        stock.AverageCostUsd.Should().Be(40m);
        stock.Provider.Should().Be("ibkr");
        stock.FlexAsOfDate.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));
        rig.Persisted.Single(h => h.Symbol == "USD Cash").InstrumentType.Should().Be("CASH");
    }

    [Fact]
    public async Task ApplyAsync_HealthyOAuthCredential_TakesPrecedence()
    {
        var rig = new Rig();
        var credential = new IBKRCredential(
            UserId, "ABCDEFGHI", "tok", "dh", [], [], [], [], [], [], [], [], [], 1);
        credential.RecordSyncSuccess();
        rig.Oauth.Setup(r => r.GetByUserIdUnscopedAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(credential);

        var count = await rig.Service.ApplyAsync(UserId, Parse(Xml(Today())));

        count.Should().Be(0);
        rig.Persisted.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyAsync_FailingOAuthCredential_FallsBackToFlex()
    {
        var rig = new Rig();
        var credential = new IBKRCredential(
            UserId, "ABCDEFGHI", "tok", "dh", [], [], [], [], [], [], [], [], [], 1);
        credential.RecordSyncError("401");
        rig.Oauth.Setup(r => r.GetByUserIdUnscopedAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(credential);

        (await rig.Service.ApplyAsync(UserId, Parse(Xml(Today())))).Should().Be(2);
    }

    [Fact]
    public async Task ApplyAsync_OldStatement_IsIgnored()
    {
        var rig = new Rig();

        (await rig.Service.ApplyAsync(UserId, Parse(Xml(Today(-30))))).Should().Be(0);
        rig.Persisted.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyAsync_StatementWithoutSections_LeavesHoldingsUntouched()
    {
        var rig = new Rig();
        var statement = Parse(Xml(Today()));
        statement.OpenPositions = null;
        statement.CashReport = null;

        (await rig.Service.ApplyAsync(UserId, statement)).Should().Be(0);
    }

    [Fact]
    public async Task ApplyAsync_CashSectionOnly_KeepsExistingStockHoldings()
    {
        var rig = new Rig();
        rig.Persisted.Add(new BrokerageHolding(UserId, "AAPL", "STK", 3m, 600m, "ibkr"));
        rig.Persisted.Add(new BrokerageHolding(UserId, "EUR Cash", "CASH", 10m, 11m, "ibkr"));
        var statement = Parse(Xml(Today()));
        statement.OpenPositions = null;

        await rig.Service.ApplyAsync(UserId, statement);

        rig.Persisted.Select(h => h.Symbol).Should().BeEquivalentTo("AAPL", "USD Cash");
    }

    [Fact]
    public async Task ApplyAsync_PositionsSectionOnly_KeepsExistingCashHoldings()
    {
        var rig = new Rig();
        rig.Persisted.Add(new BrokerageHolding(UserId, "AAPL", "STK", 3m, 600m, "ibkr"));
        rig.Persisted.Add(new BrokerageHolding(UserId, "EUR Cash", "CASH", 10m, 11m, "ibkr"));
        var statement = Parse(Xml(Today()));
        statement.CashReport = null;

        await rig.Service.ApplyAsync(UserId, statement);

        rig.Persisted.Select(h => h.Symbol).Should().BeEquivalentTo("ZZZQ", "EUR Cash");
    }

    [Fact]
    public async Task ApplyAsync_EmptyPositionsSection_RemovesSoldHoldings()
    {
        var rig = new Rig();
        rig.Persisted.Add(new BrokerageHolding(UserId, "AAPL", "STK", 3m, 600m, "ibkr"));
        var statement = Parse(Xml(Today()));
        statement.OpenPositions = [];
        statement.CashReport = null;

        await rig.Service.ApplyAsync(UserId, statement);

        rig.Persisted.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyAsync_SameSymbolInSeveralAccounts_PublishesOneAggregatedRow()
    {
        var rig = new Rig();
        var statement = Parse(Xml(Today()));
        statement.OpenPositions!.Add(new FlexOpenPositionXml
        {
            AccountId = "U0000002", Currency = "USD", AssetCategory = "STK", Symbol = "ZZZQ",
            Conid = "900000001", Position = "30", PositionValue = "1500", CostBasisPrice = "60", LevelOfDetail = "SUMMARY",
        });
        statement.CashReport!.Add(new FlexCashReportCurrencyXml
        {
            AccountId = "U0000002", Currency = "USD", EndingCash = "74.5", LevelOfDetail = "Currency",
        });

        var count = await rig.Service.ApplyAsync(UserId, statement);

        count.Should().Be(2);
        var stock = rig.Persisted.Single(h => h.Symbol == "ZZZQ");
        stock.Quantity.Should().Be(40m);
        stock.UsdValue.Should().Be(2000m);
        stock.AverageCostUsd.Should().Be(55m);
        rig.Persisted.Single(h => h.Symbol == "USD Cash").Quantity.Should().Be(200m);
    }
}
