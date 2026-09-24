using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.Alerts.Application.Services;
using FinanceSentry.Modules.Alerts.Domain;
using FinanceSentry.Modules.Alerts.Domain.Repositories;
using FinanceSentry.Modules.Alerts.Infrastructure.Persistence;
using FinanceSentry.Modules.Alerts.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Services;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.Budgets.Application.Services;
using FinanceSentry.Modules.Budgets.Domain;
using FinanceSentry.Modules.Budgets.Domain.Repositories;
using FinanceSentry.Modules.Budgets.Infrastructure.Persistence;
using FinanceSentry.Modules.Budgets.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.CryptoSync.Application.Services;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Services;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Repositories;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.Subscriptions.Domain;
using FinanceSentry.Modules.Subscriptions.Domain.Repositories;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence.Repositories;
using FinanceSentry.Integration;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using FinanceSentry.Modules.Wealth.Infrastructure.Persistence;
using FinanceSentry.Modules.Wealth.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Repositories;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence.Repositories;
using FinanceSentry.Core.Services;
using FinanceSentry.Modules.Risk.Application.Services;
using FinanceSentry.Modules.Risk.Domain;
using FinanceSentry.Modules.Risk.Domain.Repositories;
using FinanceSentry.Modules.Risk.Infrastructure.Persistence;
using FinanceSentry.Modules.Risk.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FinanceSentry.Mcp.Tests.IntegrationTests;

/// <summary>
/// Integration tests that invoke each real MCP tool against a seeded in-memory database
/// and assert structural validity of the response payload.
/// One [Fact] per tool; each builds its own isolated ServiceProvider so that seeded data
/// never leaks between tests.
/// </summary>
public sealed class ToolParityTests
{
    // ── DI helper ────────────────────────────────────────────────────────────

    // dbKey must be unique per test so that each test gets its own in-memory DB.
    private static ServiceProvider BuildProvider(
        string dbKey,
        IReadOnlyDictionary<string, IReadOnlyList<FundamentalFact>>? edgarFactsByTicker = null,
        IReadOnlyDictionary<string, QuoteCacheEntry>? quotesByTicker = null)
    {
        var services = new ServiceCollection();

        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

        // Each DbContext gets an isolated in-memory store keyed by dbKey.
        services.AddDbContext<BankSyncDbContext>(o =>
            o.UseInMemoryDatabase($"banksync-{dbKey}"));
        services.AddDbContext<CryptoSyncDbContext>(o =>
            o.UseInMemoryDatabase($"crypto-{dbKey}"));
        services.AddDbContext<BrokerageSyncDbContext>(o =>
            o.UseInMemoryDatabase($"brokerage-{dbKey}"));
        services.AddDbContext<AlertsDbContext>(o =>
            o.UseInMemoryDatabase($"alerts-{dbKey}"));
        services.AddDbContext<BudgetsDbContext>(o =>
            o.UseInMemoryDatabase($"budgets-{dbKey}"));
        services.AddDbContext<SubscriptionsDbContext>(o =>
            o.UseInMemoryDatabase($"subscriptions-{dbKey}"));
        services.AddDbContext<WealthDbContext>(o =>
            o.UseInMemoryDatabase($"wealth-{dbKey}"));
        services.AddDbContext<ResearchDbContext>(o =>
            o.UseInMemoryDatabase($"research-{dbKey}"));
        services.AddDbContext<RadarDbContext>(o =>
            o.UseInMemoryDatabase($"radar-{dbKey}"));
        services.AddDbContext<RiskDbContext>(o =>
            o.UseInMemoryDatabase($"risk-{dbKey}"));

        // Repositories — real implementations backed by the in-memory contexts above.
        services.AddScoped<IBankAccountRepository, BankAccountRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<ISyncJobRepository, SyncJobRepository>();
        services.AddScoped<ICryptoHoldingRepository, CryptoHoldingRepository>();
        services.AddScoped<IBrokerageHoldingRepository, BrokerageHoldingRepository>();
        services.AddScoped<IAlertRepository, AlertRepository>();
        services.AddScoped<IBudgetRepository, BudgetRepository>();
        services.AddScoped<IDetectedSubscriptionRepository, DetectedSubscriptionRepository>();
        services.AddScoped<INetWorthSnapshotRepository, NetWorthSnapshotRepository>();
        services.AddScoped<IThesisRepository, ThesisRepository>();
        services.AddScoped<IIpsRepository, IpsRepository>();

        // Radar: repositories + read service + options (log-only default).
        services.AddScoped<IDailyBarRepository, DailyBarRepository>();
        services.AddScoped<IRadarSignalRepository, RadarSignalRepository>();
        services.AddScoped<IRadarUniverseRepository, RadarUniverseRepository>();
        services.AddScoped<IStructureQueryService, StructureQueryService>();
        services.AddScoped<IRadarSignalWriter, RadarSignalWriter>();
        services.AddScoped<IRadarSignalReader, RadarSignalReader>();
        services.AddScoped<IMarketRegimeSource, MarketRegimeSource>();
        services.AddScoped<IRegimeReadingRepository, RegimeReadingRepository>();
        services.AddSingleton<IMarketHistorySource>(new FakeMarketHistorySource());
        services.Configure<RadarOptions>(_ => { });

        // Thesis monitor: real AlertGeneratorService (so alert side effects are exercised) backed by
        // deterministic fakes for EDGAR fundamentals / market data (no live HTTP in a parity test).
        services.AddScoped<IAlertGeneratorService, AlertGeneratorService>();
        services.AddSingleton<ISecEdgarService>(
            new FakeSecEdgarService(edgarFactsByTicker ?? new Dictionary<string, IReadOnlyList<FundamentalFact>>()));
        services.AddSingleton<IMarketDataService>(new FakeMarketDataService(quotesByTicker));
        services.AddScoped<IThesisEventRepository, ThesisEventRepository>();
        services.AddScoped<IThesisEventRecorder, ThesisEventRecorder>();
        services.AddScoped<IThesisPerformanceCalculator, ThesisPerformanceCalculator>();
        services.Configure<FrictionConfig>(_ => { });

        // Opportunity scanner (019): candidate repos + options + the two Core seams
        // (live impls over the in-memory Radar/Risk graphs already registered below).
        services.AddScoped<ICandidateRepository, CandidateRepository>();
        services.AddScoped<ICandidateScoreRepository, CandidateScoreRepository>();
        services.Configure<OpportunityOptions>(_ => { });
        services.AddScoped<IMarketStructureReader, MarketStructureReader>();
        services.AddScoped<IRiskPolicyGate, RiskPolicyGate>();

        // Research retrieval (036): real repos/chunker/retriever over the in-memory Research
        // context; embeddings stay disabled (default options) so ranking is lexical-only here.
        services.AddHttpClient();
        services.AddScoped<IResearchDocumentRepository, ResearchDocumentRepository>();
        services.AddScoped<IResearchRetrievalRepository, ResearchRetrievalRepository>();
        services.AddSingleton<IResearchChunker, ResearchChunker>();
        services.AddSingleton<IEmbeddingService, ConfiguredEmbeddingService>();
        services.AddScoped<IResearchCorpusSourceReader, ResearchCorpusSourceReader>();
        services.AddScoped<IResearchIndexer, ResearchIndexer>();
        services.AddScoped<IResearchRetriever, ResearchRetriever>();
        services.Configure<ResearchRetrievalOptions>(_ => { });

        // Cross-provider readers used by several tools.
        services.AddScoped<IBankingAccountsReader, BankingAccountsReader>();
        services.AddScoped<IBankingTotalsReader, BankingTotalsReader>();
        services.AddScoped<ICryptoHoldingsReader, CryptoHoldingsReader>();
        services.AddScoped<IBrokerageHoldingsReader, BrokerageHoldingsReader>();

        // Canonical book-figures service — single source for cash/invested/total across all surfaces.
        services.AddScoped<IBookFiguresService, BookFiguresService>();

        // Risk (022): repositories + pure evaluation services backing the 3 risk MCP tools.
        services.AddScoped<IRiskRuleSetRepository, RiskRuleSetRepository>();
        services.AddScoped<IPolicyViolationAckRepository, PolicyViolationAckRepository>();
        services.AddScoped<IHoldingSnapshotRepository, HoldingSnapshotRepository>();
        services.AddScoped<IBookSnapshotReader, BookSnapshotReader>();
        services.AddScoped<IRiskEvaluationService, RiskEvaluationService>();
        services.AddScoped<ITurnoverTracker, TurnoverTracker>();
        services.Configure<RiskOptions>(_ => { });

        // 039: cross-module read-port adapters (IPS↔Risk) from the shared composition lib, matching the
        // host graph so risk/allocation tools resolve IAllocationPolicySource / IPositionCapSource.
        services.AddCrossModulePorts();

        // Domain service + BankSync spend read port needed by GetBudgetSummaryQueryHandler.
        services.AddScoped<ICategoryNormalizationService, CategoryNormalizationService>();
        services.AddScoped<IMerchantSpendingReader, MerchantSpendingReader>();

        // Money-flow statistics stack (spec 044) needed by GetMoneyFlowStatisticsQueryHandler,
        // which GetCashflowReportTool now depends on instead of raw transactions (#675).
        services.AddScoped<ICounterpartyRepository, CounterpartyRepository>();
        services.AddScoped<ICommittedMerchantPinRepository, CommittedMerchantPinRepository>();
        services.AddScoped<IActiveSubscriptionsReader, FinanceSentry.Modules.Subscriptions.Application.Services.ActiveSubscriptionsReader>();
        services.AddScoped<ICommittedOutflowPolicy, CommittedOutflowPolicy>();
        services.AddScoped<ITransferDetectionService, TransferDetectionService>();
        services.AddScoped<ICounterpartyClassificationService, CounterpartyClassificationService>();
        services.AddScoped<IMoneyFlowStatisticsService, MoneyFlowStatisticsService>();

        // CQRS: registers all IQueryHandler / ICommandHandler / IEventHandler
        // implementations from each module assembly, wrapped in validation and logging
        // decorators.  Assemblies are resolved via the public module marker types so
        // that the correct DLL is guaranteed to be loaded.
        services.AddCqrs(
            typeof(FinanceSentry.Modules.BankSync.BankSyncModule).Assembly,
            typeof(FinanceSentry.Modules.Budgets.BudgetsModule).Assembly,
            typeof(FinanceSentry.Modules.Alerts.AlertsModule).Assembly,
            typeof(FinanceSentry.Modules.BrokerageSync.BrokerageSyncModule).Assembly,
            typeof(FinanceSentry.Modules.CryptoSync.CryptoSyncModule).Assembly,
            typeof(FinanceSentry.Modules.Subscriptions.SubscriptionsModule).Assembly,
            typeof(FinanceSentry.Modules.Wealth.WealthModule).Assembly,
            typeof(FinanceSentry.Modules.Research.ResearchModule).Assembly,
            typeof(FinanceSentry.Modules.Radar.RadarModule).Assembly,
            typeof(FinanceSentry.Modules.Risk.RiskModule).Assembly);

        services.AddSingleton<FinanceSentry.Mcp.Abstractions.IIdentityResolver>(new FakeIdentityResolver());
        services.AddScoped<GetAccountSummaryTool>();
        services.AddScoped<ListTransactionsTool>();
        services.AddScoped<GetBudgetStatusTool>();
        services.AddScoped<ListActiveAlertsTool>();
        services.AddScoped<GetPortfolioSnapshotTool>();
        services.AddScoped<ListSubscriptionsTool>();
        services.AddScoped<GetSyncHealthTool>();
        services.AddScoped<GetNetWorthHistoryTool>();
        services.AddScoped<GetCashflowReportTool>();
        services.AddScoped<GetCryptoPnlDetailTool>();
        services.AddScoped<GetTaxLotsTool>();
        services.AddScoped<RunThesisMonitorTool>();
        services.AddScoped<ListThesisBreaksTool>();
        services.AddScoped<ListThesisEventsTool>();
        services.AddScoped<SaveThesisTool>();
        services.AddScoped<GetThesisPerformanceTool>();
        services.AddScoped<GetTrackRecordTool>();
        services.AddScoped<GetPostmortemPacketTool>();
        services.AddScoped<GetMarketStructureTool>();
        services.AddScoped<GetRelativeStrengthTool>();
        services.AddScoped<GetSectorRotationTool>();
        services.AddScoped<GetMarketBreadthTool>();
        services.AddScoped<ListSignalsTool>();
        services.AddScoped<GetRadarSummaryTool>();
        services.AddScoped<GetRiskRulesTool>();
        services.AddScoped<SaveRiskRulesTool>();
        services.AddScoped<CheckRiskRulesTool>();
        services.AddScoped<AcknowledgeRiskViolationTool>();
        services.AddScoped<ScoreCandidateTool>();
        services.AddScoped<ListCandidatesTool>();
        services.AddScoped<PromoteCandidateTool>();
        services.AddScoped<RejectCandidateTool>();
        services.AddScoped<GetAllocationVsTargetTool>();

        return services.BuildServiceProvider();
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAccountSummary_ReturnsNonEmpty_WhenBankAndCryptoSeeded()
    {
        var userId = Guid.NewGuid();
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        // Seed one active TrueLayer bank account.
        var bankDb = svc.GetRequiredService<BankSyncDbContext>();
        var account = new BankAccount(userId, "ext-acc-001", "Chase", "checking", "1234", "Test User", "USD", userId, "truelayer");
        account.BeginSync();
        account.MarkActive(5_000m);
        bankDb.BankAccounts.Add(account);
        await bankDb.SaveChangesAsync();

        // Seed one crypto holding.
        var cryptoDb = svc.GetRequiredService<CryptoSyncDbContext>();
        cryptoDb.CryptoHoldings.Add(CryptoHolding.Create(userId, CryptoExchangeProvider.Binance, "BTC", 0.1m, 0m, 6_500m));
        await cryptoDb.SaveChangesAsync();

        var tool = svc.GetRequiredService<GetAccountSummaryTool>();
        var result = await tool.ExecuteAsync(userId);

        result.Should().NotBeNull();
        result.Should().HaveCountGreaterThanOrEqualTo(2);
        result.Should().AllSatisfy(e =>
        {
            e.AccountId.Should().NotBeNullOrEmpty();
            e.Provider.Should().NotBeNullOrEmpty();
            e.Currency.Should().NotBeNullOrEmpty();
        });
        result.Should().ContainSingle(e => e.Provider == "truelayer");
        result.Should().ContainSingle(e => e.Provider == "binance");
    }

    [Fact]
    public async Task ListTransactions_ReturnsNonEmpty_WhenTransactionSeeded()
    {
        var userId = Guid.NewGuid();
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        var bankDb = svc.GetRequiredService<BankSyncDbContext>();

        var account = new BankAccount(userId, "ext-txn-001", "BofA", "savings", "5678", "Test User", "USD", userId, "truelayer");
        account.BeginSync();
        account.MarkActive(2_000m);
        bankDb.BankAccounts.Add(account);

        var tx = new Transaction(account.Id, userId, 42.50m, DateTime.UtcNow, "Netflix subscription", "hash-txn-001");
        tx.MerchantCategory = "ENTERTAINMENT";
        bankDb.Transactions.Add(tx);

        await bankDb.SaveChangesAsync();

        var tool = svc.GetRequiredService<ListTransactionsTool>();
        var result = await tool.ExecuteAsync(userId);

        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
        result.Should().AllSatisfy(e =>
        {
            e.TransactionId.Should().NotBeNullOrEmpty();
            e.AccountId.Should().NotBeNullOrEmpty();
            e.Amount.Should().BeGreaterThan(0);
            e.Description.Should().NotBeNullOrEmpty();
            e.Currency.Should().NotBeNullOrEmpty();
            e.Provider.Should().NotBeNullOrEmpty();
        });
    }

    [Fact]
    public async Task GetBudgetStatus_ReturnsNonEmpty_WhenBudgetSeeded()
    {
        var userId = Guid.NewGuid();
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        // BankSyncDbContext must be initialised so that GetMerchantSpendingQueryHandler
        // can query transactions (returns empty → spending = 0, which is fine).
        _ = svc.GetRequiredService<BankSyncDbContext>();

        var budgetDb = svc.GetRequiredService<BudgetsDbContext>();
        budgetDb.Budgets.Add(Budget.Create(userId, "FOOD_AND_DRINK", 500m, "USD"));
        await budgetDb.SaveChangesAsync();

        var tool = svc.GetRequiredService<GetBudgetStatusTool>();
        var result = await tool.ExecuteAsync(userId);

        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
        result.Should().AllSatisfy(e =>
        {
            e.BudgetId.Should().NotBeNullOrEmpty();
            e.Name.Should().NotBeNullOrEmpty();
            e.Period.Should().MatchRegex(@"^\d{4}-\d{2}$");
            e.LimitAmount.Should().BeGreaterThan(0);
            e.Currency.Should().NotBeNullOrEmpty();
            e.SpentAmount.Should().BeGreaterThanOrEqualTo(0);
            e.UtilizationPercent.Should().BeGreaterThanOrEqualTo(0);
        });
    }

    [Fact]
    public async Task ListActiveAlerts_ReturnsNonEmpty_WhenUnreadAlertSeeded()
    {
        var userId = Guid.NewGuid();
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        var alertsDb = svc.GetRequiredService<AlertsDbContext>();
        alertsDb.Alerts.Add(new Alert
        {
            UserId = userId,
            Type = AlertType.LowBalance,
            Severity = AlertSeverity.Warning,
            Title = "Low balance alert",
            Message = "Your checking account balance is below $100.",
            IsRead = false,
            IsResolved = false,
            IsDismissed = false,
        });
        await alertsDb.SaveChangesAsync();

        var tool = svc.GetRequiredService<ListActiveAlertsTool>();
        var result = await tool.ExecuteAsync(userId);

        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
        result.Should().AllSatisfy(e =>
        {
            e.AlertId.Should().NotBeNullOrEmpty();
            e.Type.Should().NotBeNullOrEmpty();
            e.Severity.Should().NotBeNullOrEmpty();
            e.Title.Should().NotBeNullOrEmpty();
            e.Message.Should().NotBeNullOrEmpty();
            e.Status.Should().Be("Fired");
        });
    }

    [Fact]
    public async Task GetPortfolioSnapshot_ReturnsNonEmpty_WhenBrokerageAndCryptoHoldingsSeeded()
    {
        var userId = Guid.NewGuid();
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        var cryptoDb = svc.GetRequiredService<CryptoSyncDbContext>();
        cryptoDb.CryptoHoldings.Add(CryptoHolding.Create(userId, CryptoExchangeProvider.Binance, "ETH", 2.5m, 0m, 9_000m));
        await cryptoDb.SaveChangesAsync();

        var brokerageDb = svc.GetRequiredService<BrokerageSyncDbContext>();
        brokerageDb.BrokerageHoldings.Add(new BrokerageHolding(userId, "AAPL", "STK", 10m, 1_900m, "ibkr"));
        await brokerageDb.SaveChangesAsync();

        var tool = svc.GetRequiredService<GetPortfolioSnapshotTool>();
        var result = await tool.ExecuteAsync(userId);

        result.Should().NotBeNull();
        result.Positions.Should().HaveCountGreaterThanOrEqualTo(2);
        result.Positions.Should().AllSatisfy(e =>
        {
            e.Symbol.Should().NotBeNullOrEmpty();
            e.AssetClass.Should().NotBeNullOrEmpty();
            e.Provider.Should().NotBeNullOrEmpty();
            e.CurrentValue.Should().BeGreaterThan(0);
            e.Quantity.Should().BeGreaterThan(0);
        });
        result.Positions.Should().ContainSingle(e => e.Provider == "ibkr");
        result.Positions.Should().ContainSingle(e => e.Provider == "binance");

        // Totals roll up from the positions and stay internally consistent.
        result.InvestedValueUsd.Should().Be(result.Positions.Sum(p => p.CurrentValue));
        result.TotalValueUsd.Should().Be(result.InvestedValueUsd + result.CashUsd);
        result.TotalValueUsd.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetPortfolioSnapshot_BucketsIdleBrokerageCashAsCash_NotAsPosition()
    {
        var userId = Guid.NewGuid();
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        var brokerageDb = svc.GetRequiredService<BrokerageSyncDbContext>();
        brokerageDb.BrokerageHoldings.Add(new BrokerageHolding(userId, "AAPL", "STK", 10m, 1_900m, "ibkr"));
        // Idle currency balance — must land in cash (same bucketing as the allocation-drift tool).
        brokerageDb.BrokerageHoldings.Add(new BrokerageHolding(userId, "EUR", "CASH", 800m, 923m, "ibkr"));
        await brokerageDb.SaveChangesAsync();

        var tool = svc.GetRequiredService<GetPortfolioSnapshotTool>();
        var result = await tool.ExecuteAsync(userId);

        result.Positions.Should().ContainSingle(e => e.Symbol == "AAPL");
        result.Positions.Should().NotContain(e => e.Symbol == "EUR");
        result.BrokerageCashUsd.Should().Be(923m);
        result.CashUsd.Should().Be(result.BankingCashUsd + result.BrokerageCashUsd);
        result.InvestedValueUsd.Should().Be(1_900m);
        result.TotalValueUsd.Should().Be(result.InvestedValueUsd + result.CashUsd);
    }

    /// <summary>
    /// Parity test (AC-2): all three surfaces that previously computed book figures
    /// independently now agree exactly because they all read from one IBookFiguresService.
    /// Seeds a multi-currency book with idle brokerage cash and asserts that
    /// cashUsd / investedValueUsd / totalValueUsd are identical across all three consumers.
    /// BookSnapshotReader (the Risk surface) is verified directly since CheckRiskRulesTool
    /// does not surface the raw totals in its DTO.
    /// </summary>
    [Fact]
    public async Task BookFigures_AllThreeSurfaces_AgreeOnCashInvestedAndTotal()
    {
        var userId = Guid.NewGuid();
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        // Seed: one brokerage equity + idle EUR cash + one crypto holding + one banking account.
        var brokerageDb = svc.GetRequiredService<BrokerageSyncDbContext>();
        brokerageDb.BrokerageHoldings.Add(new BrokerageHolding(userId, "AAPL", "STK", 10m, 1_900m, "ibkr"));
        brokerageDb.BrokerageHoldings.Add(new BrokerageHolding(userId, "EUR", "CASH", 500m, 550m, "ibkr"));
        await brokerageDb.SaveChangesAsync();

        var cryptoDb = svc.GetRequiredService<CryptoSyncDbContext>();
        cryptoDb.CryptoHoldings.Add(CryptoHolding.Create(userId, CryptoExchangeProvider.Binance, "BTC", 0.05m, 0m, 3_200m));
        await cryptoDb.SaveChangesAsync();

        var bankDb = svc.GetRequiredService<BankSyncDbContext>();
        var account = new BankAccount(userId, "ext-parity-001", "Chase", "checking", "9999", "Test User", "USD", userId, "truelayer");
        account.BeginSync();
        account.MarkActive(2_000m);
        bankDb.BankAccounts.Add(account);
        await bankDb.SaveChangesAsync();

        // Expected figures (deterministic from the seed above):
        //   bankingCash    = 2_000
        //   brokerageCash  =   550  (EUR/CASH bucketed, not a position)
        //   investedValue  = 1_900 (AAPL) + 3_200 (BTC) = 5_100
        //   cashUsd        = 2_000 + 550 = 2_550
        //   totalValue     = 5_100 + 2_550 = 7_650
        const decimal expectedCashUsd = 2_550m;
        const decimal expectedInvestedUsd = 5_100m;
        const decimal expectedTotalUsd = 7_650m;

        // Surface 1: GetPortfolioSnapshot
        var snapshotTool = svc.GetRequiredService<GetPortfolioSnapshotTool>();
        var snapshot = await snapshotTool.ExecuteAsync(userId);
        snapshot.CashUsd.Should().Be(expectedCashUsd, "portfolio snapshot cash must match canonical figures");
        snapshot.InvestedValueUsd.Should().Be(expectedInvestedUsd, "portfolio snapshot invested must match canonical figures");
        snapshot.TotalValueUsd.Should().Be(expectedTotalUsd, "portfolio snapshot total must match canonical figures");

        // Surface 2: GetAllocationVsTarget — with no IPS the handler returns total via TotalValueUsd.
        var allocationTool = svc.GetRequiredService<GetAllocationVsTargetTool>();
        var drift = await allocationTool.ExecuteAsync(userId);
        drift.Should().NotBeNull();
        drift!.CashUsd.Should().Be(expectedCashUsd, "allocation drift cash must match canonical figures");
        drift.InvestedValueUsd.Should().Be(expectedInvestedUsd, "allocation drift invested must match canonical figures");
        drift.TotalValueUsd.Should().Be(expectedTotalUsd, "allocation drift total must match canonical figures");

        // Surface 3: BookSnapshotReader (the internal risk book — CheckRiskRulesTool reads via this).
        // Verified directly because CheckRiskRulesResponseDto does not surface raw totals.
        var bookReader = svc.GetRequiredService<IBookSnapshotReader>();
        var bookSnapshot = await bookReader.ReadAsync(userId);
        bookSnapshot.CashUsd.Should().Be(expectedCashUsd, "risk book snapshot cash must match canonical figures");
        bookSnapshot.InvestedUsd.Should().Be(expectedInvestedUsd, "risk book snapshot invested must match canonical figures");
        bookSnapshot.TotalUsd.Should().Be(expectedTotalUsd, "risk book snapshot total must match canonical figures");
    }

    [Fact]
    public async Task ListSubscriptions_ReturnsNonEmpty_WhenActiveSubscriptionSeeded()
    {
        var userId = Guid.NewGuid();
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        var subsDb = svc.GetRequiredService<SubscriptionsDbContext>();
        subsDb.DetectedSubscriptions.Add(DetectedSubscription.Create(
            userId.ToString(),
            "netflix",
            "Netflix",
            "monthly",
            15.99m,
            15.99m,
            "USD",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(23)),
            occurrenceCount: 3,
            confidenceScore: 85,
            category: "ENTERTAINMENT"));
        await subsDb.SaveChangesAsync();

        var tool = svc.GetRequiredService<ListSubscriptionsTool>();
        var result = await tool.ExecuteAsync(userId);

        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
        result.Should().AllSatisfy(e =>
        {
            e.SubscriptionId.Should().NotBeNullOrEmpty();
            e.Merchant.Should().NotBeNullOrEmpty();
            e.EstimatedMonthlyAmount.Should().BeGreaterThan(0);
            e.Currency.Should().NotBeNullOrEmpty();
        });
    }

    [Fact]
    public async Task GetSyncHealth_ReturnsFourProviders_WithCorrectStatusWhenSeeded()
    {
        var userId = Guid.NewGuid();
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        // Seed an active TrueLayer account so the tool reports "ok" for TrueLayer.
        var bankDb = svc.GetRequiredService<BankSyncDbContext>();
        var truelayerAccount = new BankAccount(userId, "ext-tl-001", "Chase", "checking", "9999", "Test", "USD", userId, "truelayer");
        truelayerAccount.BeginSync();
        truelayerAccount.MarkActive(10_000m);
        bankDb.BankAccounts.Add(truelayerAccount);
        await bankDb.SaveChangesAsync();

        // Seed a Binance credential with a completed sync so the tool reports "ok" for Binance.
        var cryptoDb = svc.GetRequiredService<CryptoSyncDbContext>();
        var binanceCred = ExchangeCredential.Create(
            userId,
            CryptoExchangeProvider.Binance,
            encryptedApiKey: [1, 2, 3],
            apiKeyIv: [4, 5, 6],
            apiKeyAuthTag: [7, 8, 9],
            encryptedApiSecret: [1, 2, 3],
            apiSecretIv: [4, 5, 6],
            apiSecretAuthTag: [7, 8, 9],
            keyVersion: 1);
        binanceCred.MarkSynced(DateTime.UtcNow.AddMinutes(-10));
        cryptoDb.ExchangeCredentials.Add(binanceCred);
        await cryptoDb.SaveChangesAsync();

        var tool = svc.GetRequiredService<GetSyncHealthTool>();
        var result = await tool.ExecuteAsync(userId);

        // Every provider is always returned.
        result.Should().HaveCount(5);
        result.Select(e => e.Provider).Should()
            .BeEquivalentTo(["monobank", "truelayer", "binance", "revolut_x", "ibkr"]);

        // Every entry has required fields populated.
        result.Should().AllSatisfy(e =>
        {
            e.Provider.Should().NotBeNullOrEmpty();
            e.Status.Should().NotBeNullOrEmpty();
        });

        // Seeded providers report expected statuses.
        result.Single(e => e.Provider == "truelayer").Status.Should().Be("ok");
        result.Single(e => e.Provider == "binance").Status.Should().Be("ok");

        // Providers with no credentials are honest about it.
        result.Single(e => e.Provider == "monobank").Status.Should().Be("never_synced");
        result.Single(e => e.Provider == "ibkr").Status.Should().Be("never_synced");
        result.Single(e => e.Provider == "revolut_x").Status.Should().Be("never_synced",
            "a Binance credential must not report as another venue's health");
    }

    [Fact]
    public async Task GetTaxLots_ReturnsCostBasisAndPnl_WhenBrokerageHoldingSeededWithAvgCost()
    {
        var userId = Guid.NewGuid();
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        var brokerageDb = svc.GetRequiredService<BrokerageSyncDbContext>();
        brokerageDb.BrokerageHoldings.Add(new BrokerageHolding(
            userId,
            "AAPL",
            "STK",
            10m,
            1_900m,
            "ibkr",
            averageCostUsd: 150m,
            acquiredAt: DateTime.UtcNow.AddYears(-2)));
        brokerageDb.BrokerageHoldings.Add(new BrokerageHolding(
            userId,
            "SPY",
            "STK",
            5m,
            2_500m,
            "ibkr"));
        await brokerageDb.SaveChangesAsync();

        var tool = svc.GetRequiredService<GetTaxLotsTool>();
        var result = await tool.ExecuteAsync(userId);

        result.Should().HaveCount(2);

        var aapl = result.Single(e => e.Symbol == "AAPL");
        aapl.AverageCostUsd.Should().Be(150m);
        aapl.CostBasisUsd.Should().Be(1_500m);
        aapl.UnrealizedPnlUsd.Should().Be(400m);
        aapl.IsLongTerm.Should().BeTrue();
        aapl.Provider.Should().Be("ibkr");

        var spy = result.Single(e => e.Symbol == "SPY");
        spy.AverageCostUsd.Should().BeNull();
        spy.CostBasisUsd.Should().BeNull();
        spy.UnrealizedPnlUsd.Should().BeNull();
        spy.IsLongTerm.Should().BeFalse();
    }

    [Fact]
    public async Task GetCryptoPnlDetail_ReturnsCostBasisAndUnrealizedPnl_WhenHoldingSeededWithCostBasis()
    {
        var userId = Guid.NewGuid();
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        var cryptoDb = svc.GetRequiredService<CryptoSyncDbContext>();
        var holding = CryptoHolding.Create(userId, CryptoExchangeProvider.Binance, "BTC", freeQuantity: 0.5m, lockedQuantity: 0m, usdValue: 25_000m);
        holding.SetCostBasis(
            costBasisUsd: 18_000m,
            averageBuyPriceUsd: 36_000m,
            realizedPnlUsd: 500m,
            lastTradeAt: new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc),
            tradeCount: 3);
        cryptoDb.CryptoHoldings.Add(holding);

        var nullCostHolding = CryptoHolding.Create(userId, CryptoExchangeProvider.Binance, "DOGE", freeQuantity: 1_000m, lockedQuantity: 0m, usdValue: 50m);
        cryptoDb.CryptoHoldings.Add(nullCostHolding);

        await cryptoDb.SaveChangesAsync();

        var tool = svc.GetRequiredService<GetCryptoPnlDetailTool>();
        var result = await tool.ExecuteAsync(userId);

        result.Should().HaveCount(2);

        var btc = result.Single(e => e.Asset == "BTC");
        btc.CostBasisUsd.Should().Be(18_000m);
        btc.UnrealizedPnlUsd.Should().Be(7_000m);
        btc.RealizedPnlUsd.Should().Be(500m);
        btc.TradeCount.Should().Be(3);
        btc.Provider.Should().Be("binance");

        var doge = result.Single(e => e.Asset == "DOGE");
        doge.CostBasisUsd.Should().BeNull();
        doge.UnrealizedPnlUsd.Should().BeNull();
        doge.TradeCount.Should().Be(0);
    }

    [Fact]
    public async Task GetNetWorthHistory_ReturnsSeededSnapshots()
    {
        var userId = Guid.NewGuid();
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        var wealthDb = svc.GetRequiredService<WealthDbContext>();
        wealthDb.NetWorthSnapshots.AddRange(
            new NetWorthSnapshot
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SnapshotDate = new DateOnly(2024, 1, 31),
                BankingTotal = 1_000m,
                BrokerageTotal = 500m,
                CryptoTotal = 200m,
                TotalNetWorth = 1_700m,
                Currency = "USD",
                TakenAt = DateTimeOffset.UtcNow,
            },
            new NetWorthSnapshot
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SnapshotDate = new DateOnly(2024, 2, 29),
                BankingTotal = 1_100m,
                BrokerageTotal = 600m,
                CryptoTotal = 250m,
                TotalNetWorth = 1_950m,
                Currency = "USD",
                TakenAt = DateTimeOffset.UtcNow,
            });
        await wealthDb.SaveChangesAsync();

        var tool = svc.GetRequiredService<GetNetWorthHistoryTool>();
        var result = await tool.ExecuteAsync(userId);

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(e =>
        {
            e.Currency.Should().Be("USD");
            e.TotalNetWorth.Should().BeGreaterThan(0);
        });
        result.Select(e => e.SnapshotDate).Should()
            .ContainInOrder(new DateOnly(2024, 1, 31), new DateOnly(2024, 2, 29));
    }

    [Fact]
    public async Task GetCashflowReport_AggregatesTransactionsByMonth_AndExcludesInternalTransfers()
    {
        var userId = Guid.NewGuid();
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        var bankDb = svc.GetRequiredService<BankSyncDbContext>();
        var account = new BankAccount(userId, "ext-cf-001", "Chase", "checking", "1111", "Test User", "USD", userId, "truelayer");
        account.BeginSync();
        account.MarkActive(0m);
        var savings = new BankAccount(userId, "ext-cf-002", "Chase", "savings", "2222", "Test User", "USD", userId, "truelayer");
        savings.BeginSync();
        savings.MarkActive(0m);
        bankDb.BankAccounts.AddRange(account, savings);

        // The money-flow statistics window is anchored to the current calendar month, not to
        // the tool's fromDate/toDate — the underlying service always reads "last N months from
        // now" (see MonthWindow). So the seeded data must sit in the last two complete/current
        // months rather than a fixed historical date.
        var today = DateTime.UtcNow;
        var thisMonthStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var lastMonthStart = thisMonthStart.AddMonths(-1);
        var lastMonth = lastMonthStart.AddDays(1);
        var thisMonth = thisMonthStart.AddDays(1);

        // Providers store Amount as a positive magnitude; direction lives in
        // TransactionType ("credit" = inflow, "debit" = outflow).
        var salary1 = new Transaction(account.Id, userId, 2_000m, lastMonth, "Salary", "hash-cf-001") { TransactionType = "credit" };
        var groceries = new Transaction(account.Id, userId, 300m, lastMonth, "Groceries", "hash-cf-002") { TransactionType = "debit" };
        var salary2 = new Transaction(account.Id, userId, 2_500m, thisMonth, "Salary", "hash-cf-003") { TransactionType = "credit" };
        var rent = new Transaction(account.Id, userId, 800m, thisMonth, "Rent", "hash-cf-004") { TransactionType = "debit" };

        // A transfer-like pair between the user's own accounts: equal magnitude, close dates,
        // matching descriptions — the routing rules TransferDetectionService applies. Must be
        // excluded from both inflow and outflow (#675), not counted on both sides.
        var transferOut = new Transaction(account.Id, userId, 500m, lastMonth, "Internal Transfer Savings", "hash-cf-005") { TransactionType = "debit" };
        var transferIn = new Transaction(savings.Id, userId, 500m, lastMonth, "Internal Transfer Savings", "hash-cf-006") { TransactionType = "credit" };

        bankDb.Transactions.AddRange(salary1, groceries, salary2, rent, transferOut, transferIn);
        await bankDb.SaveChangesAsync();

        var tool = svc.GetRequiredService<GetCashflowReportTool>();
        var result = await tool.ExecuteAsync(
            userId,
            fromDate: DateOnly.FromDateTime(lastMonthStart),
            toDate: DateOnly.FromDateTime(today));

        result.Should().HaveCount(2);

        var lastMonthPeriod = lastMonthStart.ToString("yyyy-MM");
        var thisMonthPeriod = thisMonthStart.ToString("yyyy-MM");

        var lastMonthEntry = result.Single(e => e.Period == lastMonthPeriod);
        lastMonthEntry.Inflow.Should().Be(2_000m);
        lastMonthEntry.Outflow.Should().Be(300m);
        lastMonthEntry.Net.Should().Be(1_700m);

        var thisMonthEntry = result.Single(e => e.Period == thisMonthPeriod);
        thisMonthEntry.Inflow.Should().Be(2_500m);
        thisMonthEntry.Outflow.Should().Be(800m);
        thisMonthEntry.Net.Should().Be(1_700m);
    }

    [Fact]
    public async Task RunThesisMonitor_MarksThesisBroken_AndSubsequentListReflectsIt()
    {
        var userId = Guid.NewGuid();

        var breachingFacts = new List<FundamentalFact>
        {
            new("MU", "GrossProfit", "GrossProfit", "USD", 30m, new DateOnly(2026, 5, 31), "Q2", 2026, "10-Q"),
            new("MU", "Revenue", "Revenue", "USD", 100m, new DateOnly(2026, 5, 31), "Q2", 2026, "10-Q"),
        };
        var factsByTicker = new Dictionary<string, IReadOnlyList<FundamentalFact>> { ["MU"] = breachingFacts };

        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"), factsByTicker);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        var researchDb = svc.GetRequiredService<ResearchDbContext>();
        researchDb.Theses.Add(new InvestmentThesis
        {
            UserId = userId,
            Ticker = "MU",
            ThesisText = "Memory upcycle",
            InvalidationTriggers =
            [
                new ThesisInvalidationTrigger(
                    FinanceSentry.Modules.Research.Domain.ThesisMonitor.ThesisMetric.GrossMargin,
                    "lessThan", 0.35m, ConsecutivePeriods: 1),
            ],
        });
        await researchDb.SaveChangesAsync();

        var runTool = svc.GetRequiredService<RunThesisMonitorTool>();
        var result = await runTool.ExecuteAsync(userId);

        result.Should().NotBeNull();
        result!.Summary.ThesesEvaluated.Should().Be(1);
        result.Summary.BreaksRaised.Should().Be(1);
        // 035: run_thesis_monitor is enriched to also return the resulting breaks in the same call.
        result.Breaks.Should().ContainSingle(b => b.Ticker == "MU");

        var listTool = svc.GetRequiredService<ListThesisBreaksTool>();
        var breaks = await listTool.ExecuteAsync(userId);

        breaks.Should().ContainSingle();
        var thesisBreak = breaks.Single();
        thesisBreak.Ticker.Should().Be("MU");
        thesisBreak.Metric.Should().Be(FinanceSentry.Modules.Research.Domain.ThesisMonitor.ThesisMetric.GrossMargin);
        thesisBreak.Reason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ListThesisBreaks_ReturnsEmpty_WhenNoThesesAreBroken()
    {
        var userId = Guid.NewGuid();
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        var listTool = svc.GetRequiredService<ListThesisBreaksTool>();
        var breaks = await listTool.ExecuteAsync(userId);

        breaks.Should().NotBeNull();
        breaks.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveThesis_RecordsCreatedEvent_WithPricedQuotes()
    {
        var userId = Guid.NewGuid();
        var quotes = new Dictionary<string, QuoteCacheEntry>
        {
            ["MU"] = new() { Ticker = "MU", Price = 100m },
            ["SPY"] = new() { Ticker = "SPY", Price = 500m },
        };
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"), quotesByTicker: quotes);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        var saveHandler = svc.GetRequiredService<ICommandHandler<
            FinanceSentry.Modules.Research.Application.Commands.SaveThesisCommand,
            FinanceSentry.Modules.Research.API.Responses.ThesisDto>>();

        var thesis = await saveHandler.Handle(
            new FinanceSentry.Modules.Research.Application.Commands.SaveThesisCommand(
                userId, null, "MU", "Memory upcycle", [], [], []),
            CancellationToken.None);

        var listTool = svc.GetRequiredService<ListThesisEventsTool>();
        var events = await listTool.ExecuteAsync(subjectId: thesis.Id, userId: userId);

        events.Should().ContainSingle();
        var created = events.Single();
        created.EventType.Should().Be(ThesisEventType.Created);
        created.PricesPending.Should().BeFalse();
        created.SubjectPrice.Should().Be(100m);
        created.BenchmarkPrice.Should().Be(500m);
    }

    [Fact]
    public async Task SaveThesis_AcceptsLongNarrativeThesisText()
    {
        var userId = Guid.NewGuid();
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;
        var thesisText = string.Join(
            " ",
            Enumerable.Repeat("Micron memory cycle recovery remains intact through pricing discipline.", 4));

        var saveTool = svc.GetRequiredService<SaveThesisTool>();
        var thesis = await saveTool.ExecuteAsync(
            ticker: "MU",
            thesisText: thesisText,
            keyDataPoints: [],
            catalysts: [],
            invalidationTriggers: [],
            userId: userId);

        thesis.Should().NotBeNull();
        thesis!.ThesisText.Should().Be(thesisText);
        thesis.ThesisText.Length.Should().BeGreaterThan(200);
    }

    [Fact]
    public async Task GetThesisPerformance_ComputesExcessReturn_AgainstLiveQuote()
    {
        var userId = Guid.NewGuid();
        var creationQuotes = new Dictionary<string, QuoteCacheEntry>
        {
            ["MU"] = new() { Ticker = "MU", Price = 100m },
            ["SPY"] = new() { Ticker = "SPY", Price = 500m },
        };
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"), quotesByTicker: creationQuotes);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        var saveHandler = svc.GetRequiredService<ICommandHandler<
            FinanceSentry.Modules.Research.Application.Commands.SaveThesisCommand,
            FinanceSentry.Modules.Research.API.Responses.ThesisDto>>();
        await saveHandler.Handle(
            new FinanceSentry.Modules.Research.Application.Commands.SaveThesisCommand(
                userId, null, "MU", "Memory upcycle", [], [], []),
            CancellationToken.None);

        var perfTool = svc.GetRequiredService<GetThesisPerformanceTool>();
        var result = await perfTool.ExecuteAsync(ticker: "MU", userId: userId);

        result.Should().NotBeNull();
        result!.IsEvaluable.Should().BeTrue();
        // Live quote == creation quote in this fixture, so absolute/benchmark/excess returns are 0.
        result.AbsoluteReturnPct.Should().Be(0m);
        result.BenchmarkReturnPct.Should().Be(0m);
        result.ExcessReturnPct.Should().Be(0m);
    }

    [Fact]
    public async Task GetTrackRecord_ReturnsLowSampleCaveat_WhenFewerThan30ClosedRecords()
    {
        var userId = Guid.NewGuid();
        var quotes = new Dictionary<string, QuoteCacheEntry>
        {
            ["MU"] = new() { Ticker = "MU", Price = 100m },
            ["SPY"] = new() { Ticker = "SPY", Price = 500m },
        };
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"), quotesByTicker: quotes);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        var saveHandler = svc.GetRequiredService<ICommandHandler<
            FinanceSentry.Modules.Research.Application.Commands.SaveThesisCommand,
            FinanceSentry.Modules.Research.API.Responses.ThesisDto>>();
        await saveHandler.Handle(
            new FinanceSentry.Modules.Research.Application.Commands.SaveThesisCommand(
                userId, null, "MU", "Memory upcycle", [], [], []),
            CancellationToken.None);

        var trackRecordTool = svc.GetRequiredService<GetTrackRecordTool>();
        var summary = await trackRecordTool.ExecuteAsync(userId: userId);

        summary.Should().NotBeNull();
        summary!.TotalCount.Should().Be(1);
        summary.LowSampleCaveat.Should().BeTrue();
        summary.ByStatus["Active"].Count.Should().Be(1);
    }

    [Fact]
    public async Task GetPostmortemPacket_ReturnsEmpty_WhenNoTerminalEventsInPeriod()
    {
        var userId = Guid.NewGuid();
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        var tool = svc.GetRequiredService<GetPostmortemPacketTool>();
        var packet = await tool.ExecuteAsync(
            periodStart: new DateOnly(2026, 1, 1), periodEnd: new DateOnly(2026, 12, 31), userId: userId);

        packet.Should().NotBeNull();
        packet!.Entries.Should().BeEmpty();
        packet.CounterfactualEntries.Should().BeEmpty();
    }

    // ── Radar (018) parity ──────────────────────────────────────────────────

    private static void SeedRadarBars(RadarDbContext db, string ticker, int count, decimal start)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-count);
        for (var i = 0; i < count; i++)
        {
            var price = start + i;
            db.DailyBars.Add(new FinanceSentry.Modules.Radar.Domain.DailyBar
            {
                Id = Guid.NewGuid(),
                Ticker = ticker,
                Date = date.AddDays(i),
                Open = price,
                High = price + 1,
                Low = price - 1,
                Close = price,
                AdjClose = price,
                Volume = 1_000_000 + i,
            });
        }
    }

    private static async Task SeedRadarUniverseAsync(IServiceProvider svc)
    {
        var db = svc.GetRequiredService<RadarDbContext>();
        SeedRadarBars(db, "SPY", 260, 400m);
        SeedRadarBars(db, "NVDA", 260, 100m);
        db.UniverseMembers.Add(new RadarUniverseMember { Id = Guid.NewGuid(), Ticker = "SPY", Kind = UniverseKind.Benchmark, Source = UniverseSource.Seed, Active = true });
        db.UniverseMembers.Add(new RadarUniverseMember { Id = Guid.NewGuid(), Ticker = "NVDA", Kind = UniverseKind.Holding, Source = UniverseSource.Auto, Active = true });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetMarketStructure_ReturnsMetrics_WhenBarsSeeded()
    {
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;
        await SeedRadarUniverseAsync(svc);

        var tool = svc.GetRequiredService<GetMarketStructureTool>();
        var result = await tool.ExecuteAsync("NVDA");

        result.Should().NotBeNull();
        result!.Ticker.Should().Be("NVDA");
        result.ReturnByWindow[21].Should().NotBeNull();
    }

    [Fact]
    public async Task GetRelativeStrength_ReturnsUniverse_WhenBarsSeeded()
    {
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;
        await SeedRadarUniverseAsync(svc);

        var tool = svc.GetRequiredService<GetRelativeStrengthTool>();
        var result = await tool.ExecuteAsync();

        result.Should().NotBeNull();
        result.Should().Contain(s => s.Ticker == "NVDA");
    }

    [Fact]
    public async Task GetSectorRotation_ReturnsRows_WithoutThrowing()
    {
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;
        await SeedRadarUniverseAsync(svc);

        var tool = svc.GetRequiredService<GetSectorRotationTool>();
        var result = await tool.ExecuteAsync();

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetMarketBreadth_ReturnsEvaluatedCount_WhenBarsSeeded()
    {
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;
        await SeedRadarUniverseAsync(svc);

        var tool = svc.GetRequiredService<GetMarketBreadthTool>();
        var result = await tool.ExecuteAsync();

        result.Should().NotBeNull();
        result.Evaluated.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ListSignals_ReturnsSeededSignal()
    {
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        var db = svc.GetRequiredService<RadarDbContext>();
        db.RadarSignals.Add(new RadarSignal
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Scanner = "market_structure",
            SignalType = "unusual_move",
            Severity = FinanceSentry.Core.Interfaces.SignalSeverity.Notable,
            SubjectType = "Ticker",
            Subject = "NVDA",
            DedupKey = "market_structure:unusual_move:NVDA:today",
            Payload = new Dictionary<string, object> { ["zScore"] = 3.4m },
            PayloadVersion = 1,
        });
        await db.SaveChangesAsync();

        var tool = svc.GetRequiredService<ListSignalsTool>();
        var result = await tool.ExecuteAsync(scanner: "market_structure");

        result.Should().NotBeNull();
        result.Should().ContainSingle(s => s.Subject == "NVDA" && s.SignalType == "unusual_move");
    }

    // Feature 043 US2: portfolio scanner signals must be queryable through the existing
    // list_signals MCP tool using scanner="portfolio_scanner".
    [Fact]
    public async Task ListSignals_ReturnsPortfolioScannerSignal_WhenSeeded()
    {
        var userId = Guid.NewGuid();
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        var db = svc.GetRequiredService<RadarDbContext>();
        db.RadarSignals.Add(new RadarSignal
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Scanner = FinanceSentry.Modules.Radar.Domain.RadarScanners.Portfolio,
            SignalType = FinanceSentry.Modules.Radar.Domain.RadarSignalTypes.AllocationDrift,
            Severity = FinanceSentry.Core.Interfaces.SignalSeverity.Notable,
            SubjectType = FinanceSentry.Modules.Radar.Domain.RadarSubjectTypes.AssetClass,
            Subject = "Equity",
            UserId = userId,
            DedupKey = $"portfolio_scanner:allocation_drift:{userId:N}:Equity:{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}",
            Payload = new Dictionary<string, object> { ["driftPct"] = 10m },
            PayloadVersion = 1,
        });
        await db.SaveChangesAsync();

        var tool = svc.GetRequiredService<ListSignalsTool>();
        var result = await tool.ExecuteAsync(
            scanner: FinanceSentry.Modules.Radar.Domain.RadarScanners.Portfolio,
            userId: userId);

        result.Should().NotBeNull();
        result.Should().ContainSingle(s =>
            s.SignalType == FinanceSentry.Modules.Radar.Domain.RadarSignalTypes.AllocationDrift
            && s.Subject == "Equity");
    }

    [Fact]
    public async Task GetRadarSummary_ReturnsSnapshot_WhenBarsSeeded()
    {
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;
        await SeedRadarUniverseAsync(svc);

        var tool = svc.GetRequiredService<GetRadarSummaryTool>();
        var result = await tool.ExecuteAsync();

        result.Should().NotBeNull();
        result.Breadth.Should().NotBeNull();
    }

    // ── Risk (022) parity ─────────────────────────────────────────────────────

    [Fact]
    public async Task SaveRiskRules_ThenGetRiskRules_RoundTripsCurrentVersion()
    {
        var userId = Guid.NewGuid();
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        var saveTool = svc.GetRequiredService<SaveRiskRulesTool>();
        var saved = await saveTool.ExecuteAsync(maxPositionWeightPct: 0.25m, userId: userId);

        saved.Should().NotBeNull();
        saved!.Version.Should().Be(1);
        saved.MaxPositionWeightPct.Should().Be(0.25m);

        var getTool = svc.GetRequiredService<GetRiskRulesTool>();
        var current = await getTool.ExecuteAsync(userId);

        current.Should().NotBeNull();
        current!.MaxPositionWeightPct.Should().Be(0.25m);
    }

    [Fact]
    public async Task CheckRiskRules_NoProposal_ReturnsComplianceReport_WithConcentrationViolation()
    {
        var userId = Guid.NewGuid();
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        // One dominant brokerage position (100% of book) against a 25% cap → one violation.
        var brokerageDb = svc.GetRequiredService<BrokerageSyncDbContext>();
        brokerageDb.BrokerageHoldings.Add(new BrokerageHolding(userId, "DRAM", "STK", 100m, 6_900m, "ibkr"));
        await brokerageDb.SaveChangesAsync();

        var saveTool = svc.GetRequiredService<SaveRiskRulesTool>();
        await saveTool.ExecuteAsync(maxPositionWeightPct: 0.25m, userId: userId);

        var checkTool = svc.GetRequiredService<CheckRiskRulesTool>();
        var result = await checkTool.ExecuteAsync(userId: userId);

        result.Should().NotBeNull();
        result!.HasRuleSet.Should().BeTrue();
        result.Violations.Should().NotBeNull();
        result.Violations!.Should().ContainSingle(v => v.RuleKey == RiskRuleKeys.MaxPositionWeight && v.Subject == "DRAM");
    }

    [Fact]
    public async Task CheckRiskRules_Proposal_ReturnsRefusedVerdict_WhenBreachingConcentration()
    {
        var userId = Guid.NewGuid();
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        // Cash-only book of $10k; a $5k new position would be 33% of the projected book vs a 25% cap.
        var bankDb = svc.GetRequiredService<BankSyncDbContext>();
        var account = new BankAccount(userId, "ext-risk-001", "Chase", "checking", "1234", "Test", "USD", userId, "truelayer");
        account.BeginSync();
        account.MarkActive(10_000m);
        bankDb.BankAccounts.Add(account);
        await bankDb.SaveChangesAsync();

        var saveTool = svc.GetRequiredService<SaveRiskRulesTool>();
        await saveTool.ExecuteAsync(maxPositionWeightPct: 0.25m, userId: userId);

        var checkTool = svc.GetRequiredService<CheckRiskRulesTool>();
        var verdict = await checkTool.ExecuteAsync(ticker: "NVDA", proposedUsd: 5_000m, userId: userId);

        verdict.Should().NotBeNull();
        verdict!.Decision.Should().Be(RiskDecision.Refused);
        verdict.RuleKey.Should().Be(RiskRuleKeys.MaxPositionWeight);
        verdict.MaxCompliantSizeUsd.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckRiskRules_Proposal_UsesCurrentValueBook_ForMinCashBuffer()
    {
        var userId = Guid.NewGuid();
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        var bankDb = svc.GetRequiredService<BankSyncDbContext>();
        var account = new BankAccount(userId, "ext-risk-cash", "Chase", "checking", "1234", "Test", "USD", userId, "truelayer");
        account.BeginSync();
        account.MarkActive(2_446m);
        bankDb.BankAccounts.Add(account);

        var cryptoHolding = CryptoHolding.Create(userId, CryptoExchangeProvider.Binance, "SOL", 10m, 0m, 12_054m);
        cryptoHolding.SetCostBasis(100_000m, 10_000m, null, DateTime.UtcNow, 1);
        var cryptoDb = svc.GetRequiredService<CryptoSyncDbContext>();
        cryptoDb.CryptoHoldings.Add(cryptoHolding);

        await bankDb.SaveChangesAsync();
        await cryptoDb.SaveChangesAsync();

        var saveTool = svc.GetRequiredService<SaveRiskRulesTool>();
        await saveTool.ExecuteAsync(minCashBufferPct: 0.10m, userId: userId);

        var checkTool = svc.GetRequiredService<CheckRiskRulesTool>();

        // $800 fits within the 10% buffer against the $14,500 market-value book: Allowed.
        var verdict = await checkTool.ExecuteAsync(ticker: "MHPC", proposedUsd: 800m, userId: userId);
        verdict.Should().NotBeNull();
        verdict!.Decision.Should().Be(RiskDecision.Allowed);
        verdict.RuleKey.Should().NotBe(RiskRuleKeys.MinCashBuffer);

        // Headroom = cash − (10% × totalBook) = $2,446 − $1,450 = $996.
        // A proposal just above headroom must Refuse and expose the ≈$996 figure,
        // proving the denominator is current market value, not phantom cost basis.
        var refused = await checkTool.ExecuteAsync(ticker: "MHPC", proposedUsd: 997m, userId: userId);
        refused.Should().NotBeNull();
        refused!.Decision.Should().Be(RiskDecision.Refused);
        refused.RuleKey.Should().Be(RiskRuleKeys.MinCashBuffer);
        refused.HeadroomUsd.Should().BeApproximately(996m, 10m, "headroom is based on current market value, not phantom cost basis");
    }

    // ── Opportunity scanner (019) parity ──────────────────────────────────────

    [Fact]
    public async Task ScoreCandidate_CreatesCandidate_WithExplainableScorecard()
    {
        var userId = Guid.NewGuid();
        var facts = new Dictionary<string, IReadOnlyList<FundamentalFact>>
        {
            ["MSFT"] =
            [
                new("MSFT", "Revenue", "Revenue", "USD", 100m, new DateOnly(2026, 3, 31), "Q3", 2026, "10-Q"),
                new("MSFT", "Revenue", "Revenue", "USD", 80m, new DateOnly(2025, 3, 31), "Q3", 2025, "10-Q"),
                new("MSFT", "GrossProfit", "GrossProfit", "USD", 70m, new DateOnly(2026, 3, 31), "Q3", 2026, "10-Q"),
            ],
        };
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"), facts);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        var tool = svc.GetRequiredService<ScoreCandidateTool>();
        var result = await tool.ExecuteAsync("MSFT", userId: userId);

        result.Should().NotBeNull();
        result!.Ticker.Should().Be("MSFT");
        result.IsNewCandidate.Should().BeTrue();
        // Fundamentals evaluable from the seeded facts; every sub-score cites its evidence.
        result.Scorecard.FundamentalsScore.Should().NotBeNull();
        result.Scorecard.Evidence.RevenueYoy.Should().NotBeNull();
    }

    [Fact]
    public async Task ListCandidates_ReturnsScoredCandidate_AfterScoring()
    {
        var userId = Guid.NewGuid();
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        var scoreTool = svc.GetRequiredService<ScoreCandidateTool>();
        await scoreTool.ExecuteAsync("NVDA", userId: userId);

        var listTool = svc.GetRequiredService<ListCandidatesTool>();
        var result = await listTool.ExecuteAsync(userId: userId);

        result.Should().ContainSingle(c => c.Ticker == "NVDA" && c.Status == CandidateStatus.Active);
    }

    [Fact]
    public async Task PromoteCandidate_CreatesThesis_WhenGateAllows()
    {
        var userId = Guid.NewGuid();
        var quotes = new Dictionary<string, QuoteCacheEntry>
        {
            ["AMD"] = new() { Ticker = "AMD", Price = 100m },
            ["SPY"] = new() { Ticker = "SPY", Price = 500m },
        };
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"), quotesByTicker: quotes);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        var scoreTool = svc.GetRequiredService<ScoreCandidateTool>();
        var scored = await scoreTool.ExecuteAsync("AMD", userId: userId);

        var promoteTool = svc.GetRequiredService<PromoteCandidateTool>();
        // No rule set on file → gate Allowed → thesis created.
        var result = await promoteTool.ExecuteAsync(id: scored!.CandidateId, userId: userId);

        result.Should().NotBeNull();
        result!.Gate.Decision.Should().Be(RiskGateDecision.Allowed);
        result.ThesisId.Should().NotBeNull();

        var researchDb = svc.GetRequiredService<ResearchDbContext>();
        var candidate = await researchDb.OpportunityCandidates.FirstAsync(c => c.Id == scored.CandidateId);
        candidate.Status.Should().Be(CandidateStatus.Promoted);
        candidate.PromotedThesisId.Should().Be(result.ThesisId);
    }

    [Fact]
    public async Task RejectCandidate_MarksRejected_WithReason()
    {
        var userId = Guid.NewGuid();
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        var scoreTool = svc.GetRequiredService<ScoreCandidateTool>();
        var scored = await scoreTool.ExecuteAsync("INTC", userId: userId);

        var rejectTool = svc.GetRequiredService<RejectCandidateTool>();
        var result = await rejectTool.ExecuteAsync(id: scored!.CandidateId, reason: "valuation too rich", userId: userId);

        result.Should().NotBeNull();
        result!.CandidateFound.Should().BeTrue();
        result.Status.Should().Be(CandidateStatus.Rejected);

        var listTool = svc.GetRequiredService<ListCandidatesTool>();
        var listed = await listTool.ExecuteAsync(status: CandidateStatus.Rejected, userId: userId);
        listed.Should().ContainSingle(c => c.Ticker == "INTC" && c.RejectedReason == "valuation too rich");
    }

    // ── Research retrieval (036) ─────────────────────────────────────────────
    // These tools take no userId parameter by contract, so each fact constructs the tool over the
    // real handler graph with an identity-bearing FakeIdentityResolver instead of resolving it.

    private static async Task<Guid> SeedIndexedResearchDocumentAsync(
        ResearchDbContext researchDb,
        Guid? ownerUserId,
        string title,
        string text,
        string ticker)
    {
        var document = new ResearchDocument
        {
            SourceType = ResearchDocumentSourceType.NewsArticle,
            SourceId = Guid.NewGuid().ToString(),
            UserId = ownerUserId,
            Title = title,
            Text = text,
            ContentHash = FinanceSentry.Modules.Research.Application.Services.ResearchChunker
                .ComputeContentHash($"{title}\n{text}"),
            Tickers = [ticker],
            IndexStatus = ResearchIndexStatus.Indexed,
            PublishedAt = DateTimeOffset.UtcNow.AddHours(-2),
        };
        researchDb.ResearchDocuments.Add(document);
        researchDb.ResearchChunks.Add(new ResearchChunk
        {
            DocumentId = document.Id,
            Ordinal = 0,
            Text = text,
            ContentHash = FinanceSentry.Modules.Research.Application.Services.ResearchChunker
                .ComputeContentHash(text),
            TokenEstimate = text.Length / 4,
        });
        await researchDb.SaveChangesAsync();
        return document.Id;
    }

    [Fact]
    public async Task SearchResearchCorpus_ReturnsCitedChunks_AndHidesOtherUsersPrivateDocs()
    {
        var userId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        var researchDb = svc.GetRequiredService<ResearchDbContext>();
        var globalDocId = await SeedIndexedResearchDocumentAsync(
            researchDb, null, "DRAM contract prices recover",
            "Contract pricing improved for another quarter across memory suppliers.", "MU");
        await SeedIndexedResearchDocumentAsync(
            researchDb, strangerId, "Private MU note",
            "Private conviction notes about memory pricing.", "MU");

        var tool = new SearchResearchCorpusTool(
            svc.GetRequiredService<IQueryHandler<
                FinanceSentry.Modules.Research.Application.Queries.SearchResearchCorpusQuery,
                FinanceSentry.Modules.Research.API.Responses.ResearchSearchResultDto>>(),
            new FakeIdentityResolver { ResolvedUserId = userId });

        var result = await tool.ExecuteAsync("memory contract pricing", tickers: ["MU"]);

        result.Results.Should().NotBeEmpty();
        result.Results.Should().OnlyContain(r => r.DocumentId == globalDocId);
        result.Results[0].SourceType.Should().Be("NewsArticle");
        result.Results[0].ChunkId.Should().NotBeEmpty();
        result.Results[0].Snippet.Should().NotBeNullOrEmpty();
        result.Results[0].CombinedScore.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetResearchContext_ReturnsGroupedCitedPacket_ForTicker()
    {
        var userId = Guid.NewGuid();
        await using var sp = BuildProvider(Guid.NewGuid().ToString("N"));
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider;

        var researchDb = svc.GetRequiredService<ResearchDbContext>();
        await SeedIndexedResearchDocumentAsync(
            researchDb, null, "MU research evidence roundup",
            "Fresh research evidence on MU covering recent developments in memory pricing.", "MU");

        var tool = new GetResearchContextTool(
            svc.GetRequiredService<IQueryHandler<
                FinanceSentry.Modules.Research.Application.Queries.GetResearchContextQuery,
                FinanceSentry.Modules.Research.API.Responses.ResearchContextPacketDto>>(),
            new FakeIdentityResolver { ResolvedUserId = userId });

        var packet = await tool.ExecuteAsync(ticker: "MU");

        packet.Should().NotBeNull();
        packet!.SubjectType.Should().Be("Ticker");
        packet.Ticker.Should().Be("MU");
        packet.Thesis.Should().BeNull("no thesis is seeded for the ticker");
        packet.Groups.Should().ContainSingle(g => g.Name == "recent_news");
        packet.Groups.SelectMany(g => g.Items)
            .Should().OnlyContain(i => i.DocumentId != Guid.Empty && i.ChunkId != Guid.Empty);
    }
}
