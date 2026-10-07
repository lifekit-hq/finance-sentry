namespace FinanceSentry.Tests.Integration.BrokerageSync;

using System.Globalization;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// The Flex holdings sync through the real repositories on real Postgres, schema built by the module's
/// own migrations (including the <c>FlexAsOfDate</c> column): a Flex-only user's statement lands as
/// holdings, a multi-account statement does not trip the (user, symbol, provider) unique index, and a
/// re-sync reconciles sold positions. All data is synthetic.
/// </summary>
[Trait("Category", "Integration")]
public sealed class IbkrFlexHoldingsSyncPostgresTests : IAsyncLifetime
{
    private readonly Guid _user = Guid.NewGuid();
    private TestDatabase? _database;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres16.CreateDatabaseAsync();

        await using var setup = CreateContext();
        await setup.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    private BrokerageSyncDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<BrokerageSyncDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(null));

    private static string Today() => DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private static FlexStatementXml Statement(string openPositions, string cashReport)
    {
        var xml = $"""
            <FlexQueryResponse queryName="SyntheticQuery" type="AF">
              <FlexStatements count="1">
                <FlexStatement accountId="U0000001" fromDate="{Today()}" toDate="{Today()}" period="LastBusinessDay">
                  {openPositions}
                  {cashReport}
                </FlexStatement>
              </FlexStatements>
            </FlexQueryResponse>
            """;
        return IbkrFlexClient.DeserializeStatement(xml).FlexStatements.Items[0];
    }

    private async Task<int> ApplyAsync(FlexStatementXml statement)
    {
        await using var ctx = CreateContext();
        var service = new IbkrFlexHoldingsSyncService(
            new IBKRCredentialRepository(ctx), new BrokerageHoldingRepository(ctx),
            new BrokerageInstrumentRepository(ctx), NullLogger<IbkrFlexHoldingsSyncService>.Instance);
        return await service.ApplyAsync(_user, statement);
    }

    private async Task<List<Modules.BrokerageSync.Domain.BrokerageHolding>> HoldingsAsync()
    {
        await using var ctx = CreateContext();
        return await new BrokerageHoldingRepository(ctx).GetByUserIdUnscopedAsync(_user) is var list
            ? [.. list]
            : [];
    }

    [DockerRequiredFact]
    public async Task FlexOnlyUser_MultiAccountStatement_PublishesAggregatedHoldingsAndCash_ThenReconcilesSoldPositions()
    {
        var first = Statement(
            """
            <OpenPositions>
              <OpenPosition accountId="U0000001" currency="USD" assetCategory="STK" symbol="ZZZQ" conid="900000001" position="10" positionValue="500" costBasisPrice="40" levelOfDetail="SUMMARY" />
              <OpenPosition accountId="U0000002" currency="USD" assetCategory="STK" symbol="ZZZQ" conid="900000001" position="5" positionValue="250" costBasisPrice="55" levelOfDetail="SUMMARY" />
              <OpenPosition accountId="U0000001" currency="USD" assetCategory="STK" symbol="YYYQ" conid="900000002" position="3" positionValue="90" costBasisPrice="20" levelOfDetail="SUMMARY" />
            </OpenPositions>
            """,
            """
            <CashReport>
              <CashReportCurrency accountId="U0000001" currency="USD" endingCash="100" levelOfDetail="Currency" />
              <CashReportCurrency accountId="U0000002" currency="USD" endingCash="25.5" levelOfDetail="Currency" />
              <CashReportCurrency accountId="U0000001" currency="BASE_SUMMARY" endingCash="125.5" levelOfDetail="BaseCurrency" />
            </CashReport>
            """);

        (await ApplyAsync(first)).Should().Be(3);

        var holdings = await HoldingsAsync();
        holdings.Select(h => h.Symbol).Should().BeEquivalentTo("ZZZQ", "YYYQ", "USD Cash");
        var zzzq = holdings.Single(h => h.Symbol == "ZZZQ");
        zzzq.Quantity.Should().Be(15m);
        zzzq.UsdValue.Should().Be(750m);
        zzzq.AverageCostUsd.Should().BeApproximately(45m, 0.0001m);
        zzzq.FlexAsOfDate.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));
        zzzq.InstrumentId.Should().NotBeNull();
        holdings.Single(h => h.Symbol == "USD Cash").UsdValue.Should().Be(125.5m);

        // The next day's statement: YYYQ sold, ZZZQ unchanged. A repeat sync must not duplicate rows.
        var second = Statement(
            """
            <OpenPositions>
              <OpenPosition accountId="U0000001" currency="USD" assetCategory="STK" symbol="ZZZQ" conid="900000001" position="15" positionValue="780" costBasisPrice="45" levelOfDetail="SUMMARY" />
            </OpenPositions>
            """,
            """
            <CashReport>
              <CashReportCurrency accountId="U0000001" currency="USD" endingCash="125.5" levelOfDetail="Currency" />
            </CashReport>
            """);

        (await ApplyAsync(second)).Should().Be(2);

        var after = await HoldingsAsync();
        after.Select(h => h.Symbol).Should().BeEquivalentTo("ZZZQ", "USD Cash");
        after.Single(h => h.Symbol == "ZZZQ").UsdValue.Should().Be(780m);
    }

    [DockerRequiredFact]
    public async Task FlexOnlyUser_LotOnlyOpenPositions_KeepsHoldingsOnEverySync()
    {
        var lotOnly = Statement(
            """
            <OpenPositions>
              <OpenPosition accountId="U0000001" currency="USD" assetCategory="STK" symbol="ZZZQ" conid="900000001" position="4" positionValue="200" costBasisPrice="40" levelOfDetail="LOT" />
              <OpenPosition accountId="U0000001" currency="USD" assetCategory="STK" symbol="ZZZQ" conid="900000001" position="6" positionValue="300" costBasisPrice="50" levelOfDetail="LOT" />
            </OpenPositions>
            """,
            string.Empty);

        await ApplyAsync(lotOnly);
        await ApplyAsync(lotOnly);

        var holdings = await HoldingsAsync();
        var zzzq = holdings.Should().ContainSingle().Subject;
        zzzq.Quantity.Should().Be(10m);
        zzzq.UsdValue.Should().Be(500m);
    }

    [DockerRequiredFact]
    public async Task CashOnlyStatement_NeverWipesExistingStockHoldings()
    {
        await ApplyAsync(Statement(
            """
            <OpenPositions>
              <OpenPosition accountId="U0000001" currency="USD" assetCategory="STK" symbol="ZZZQ" conid="900000001" position="10" positionValue="500" costBasisPrice="40" levelOfDetail="SUMMARY" />
            </OpenPositions>
            """,
            string.Empty));

        await ApplyAsync(Statement(
            string.Empty,
            """
            <CashReport>
              <CashReportCurrency accountId="U0000001" currency="USD" endingCash="10" levelOfDetail="Currency" />
            </CashReport>
            """));

        (await HoldingsAsync()).Select(h => h.Symbol).Should().BeEquivalentTo("ZZZQ", "USD Cash");
    }

    [DockerRequiredFact]
    public async Task StatementWithNeitherSection_NeverWipesExistingHoldings()
    {
        await ApplyAsync(Statement(
            """
            <OpenPositions>
              <OpenPosition accountId="U0000001" currency="USD" assetCategory="STK" symbol="ZZZQ" conid="900000001" position="10" positionValue="500" costBasisPrice="40" levelOfDetail="SUMMARY" />
            </OpenPositions>
            """,
            string.Empty));

        (await ApplyAsync(Statement(string.Empty, string.Empty))).Should().Be(0);

        (await HoldingsAsync()).Select(h => h.Symbol).Should().BeEquivalentTo("ZZZQ");
    }
}
