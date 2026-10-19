namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>M5: the net-worth bridge wired over the snapshots, the sleeves' growth, the honest flow and the bank balances.</summary>
public class GetNetWorthBridgeQueryHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly D0 = new(2026, 9, 1);

    private readonly Mock<INetWorthSnapshotRepository> _snapshots = new();
    private readonly Mock<IQueryHandler<GetSleeveGrowthQuery, SleeveGrowthResponse>> _sleeves = new();
    private readonly Mock<IHonestMonthlyFlowReader> _honestFlow = new();
    private readonly Mock<IBankingAccountsReader> _accounts = new();
    private readonly Mock<IBankingTransactionReader> _transactions = new();
    private readonly Mock<IHistoricalExchangeRateService> _rates = new();
    private readonly Mock<IAccountScopeChangeReader> _bankLinks = new();
    private readonly Mock<IAccountScopeChangeReader> _brokerageLinks = new();
    private readonly Dictionary<DateOnly, decimal> _eur = [];

    public GetNetWorthBridgeQueryHandlerTests()
    {
        _snapshots.Setup(r => r.GetSourcesAsync(UserId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _sleeves.Setup(h => h.Handle(It.IsAny<GetSleeveGrowthQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(Sleeve());
        _accounts.Setup(a => a.GetActiveAccountSnapshotsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _transactions.Setup(t => t.GetTransactionsAsync(UserId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _rates.Setup(r => r.GetPublishedSeriesAsync("EUR", It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _eur);
        _bankLinks.SetupGet(r => r.WealthSourceId).Returns(WealthSourceIds.Banking);
        _bankLinks.Setup(r => r.GetChangesSinceAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _brokerageLinks.SetupGet(r => r.WealthSourceId).Returns(WealthSourceIds.Brokerage);
        _brokerageLinks.Setup(r => r.GetChangesSinceAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
    }

    private static DateOnly Day(int n) => D0.AddDays(n);

    private static NetWorthSnapshot Close(int day, decimal banking, decimal brokerage = 0m, decimal crypto = 0m, decimal other = 0m)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            SnapshotDate = Day(day),
            BankingTotal = banking,
            BrokerageTotal = brokerage,
            CryptoTotal = crypto,
            TotalNetWorth = banking + brokerage + crypto + other,
        };

    private static SleeveGrowthResponse Sleeve(
        decimal? growth = null, decimal income = 0m, decimal fees = 0m, decimal taxes = 0m, decimal linked = 0m)
        => new(
            Day(0), Day(0), Day(10), null, null, null, growth is null ? null : linked, growth,
            growth is null ? null : income, growth is null ? null : fees, growth is null ? null : taxes, null, [],
            new Headline(growth, null, HeadlineMethod.ContributionsVsGrowth, null, "sleeve", new HeadlineWindow(Day(0), Day(10)),
                HeadlineQuality.Complete, null, null, [], null, "USD"));

    private void Setup(decimal savings, params NetWorthSnapshot[] closes)
    {
        _snapshots.Setup(r => r.GetByUserIdAsync(UserId, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(closes);
        var (from, to) = (closes[0].SnapshotDate.AddDays(1), closes[^1].SnapshotDate);
        _honestFlow.Setup(f => f.GetWindowNetAsync(UserId, from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(savings);
    }

    private void SetupSleeve(string sleeve, SleeveGrowthResponse response)
        => _sleeves.Setup(h => h.Handle(It.Is<GetSleeveGrowthQuery>(q => q.Sleeve == sleeve), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

    private Task<NetWorthBridgeResponse> Handle()
        => new GetNetWorthBridgeQueryHandler(
                _snapshots.Object, _sleeves.Object, _honestFlow.Object, _accounts.Object, _transactions.Object, _rates.Object,
                [_bankLinks.Object, _brokerageLinks.Object])
            .Handle(new GetNetWorthBridgeQuery(UserId), CancellationToken.None);

    private static decimal? Explained(NetWorthBridgeResponse r)
        => r.Savings + r.InvestmentReturn + r.FxTranslation + r.Revaluation + r.NewlyLinked - r.Unlinked;

    [Fact]
    public async Task TheLinesSumExactlyToEndMinusStart_TheResidualIncluded()
    {
        // Banking moves 257 on 250 of savings; brokerage grows 80 (10 of it a dividend, after 2 of fees and 1 of tax)
        // and gains a 50 link; crypto grows 5 and loses a 30 removal; a manual value is revalued by 50.
        Setup(
            250m,
            Close(0, banking: 1_000m, brokerage: 500m, crypto: 100m, other: 300m),
            Close(10, banking: 1_257m, brokerage: 630m, crypto: 75m, other: 350m));
        SetupSleeve(WealthSourceIds.Brokerage, Sleeve(80m, income: 10m, fees: 2m, taxes: 1m, linked: 50m));
        SetupSleeve(WealthSourceIds.Crypto, Sleeve(5m, linked: -30m));

        var result = await Handle();

        result.From.Should().Be(Day(0));
        result.To.Should().Be(Day(10));
        result.StartValue.Should().Be(1_900m);
        result.EndValue.Should().Be(2_312m);
        result.Savings.Should().Be(250m);
        result.Market.Should().Be(78m);
        result.Income.Should().Be(10m);
        result.Fees.Should().Be(2m);
        result.Taxes.Should().Be(1m);
        result.InvestmentReturn.Should().Be(85m);
        result.FxTranslation.Should().Be(0m);
        result.Revaluation.Should().Be(50m);
        result.NewlyLinked.Should().Be(50m);
        result.Unlinked.Should().Be(30m);
        result.Residual.Should().Be(7m);
        (Explained(result) + result.Residual).Should().Be(result.EndValue - result.StartValue);
    }

    [Theory]
    [InlineData("10120", false)]
    [InlineData("10120.01", true)]
    public async Task AResidualAboveTwoTenthsOfAPercentOfTheStart_DegradesTheBridge(string end, bool degraded)
    {
        // 20 of residual on a 10,000 start is exactly the tolerance: still complete. A cent more degrades it.
        Setup(100m, Close(0, banking: 10_000m), Close(10, banking: decimal.Parse(end, System.Globalization.CultureInfo.InvariantCulture)));

        var result = await Handle();

        (Explained(result) + result.Residual).Should().Be(result.EndValue - result.StartValue);
        result.ResidualAboveTolerance.Should().Be(degraded);
        if (degraded)
        {
            result.Residual.Should().Be(20.01m);
            result.Quality.Should().Be(HeadlineQuality.Degraded);
            result.Reasons.Should().ContainSingle(r => r.Code == HeadlineReasonCode.ResidualAboveTolerance);
        }
        else
        {
            result.Residual.Should().Be(20m);
            result.Quality.Should().Be(HeadlineQuality.Complete);
            result.Reasons.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task AWindowWithARateMoveAndANewlyLinkedAccount_BridgesWithNoResidual()
    {
        var held = Guid.NewGuid();
        var linked = Guid.NewGuid();
        _eur[Day(0)] = 1.10m;
        _eur[Day(4)] = 1.15m;
        _eur[Day(6)] = 1.15m;
        _eur[Day(10)] = 1.20m;
        _accounts.Setup(a => a.GetActiveAccountSnapshotsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AccountBalanceSnapshot(held, "AIB", "checking", "1111", "EUR", 1_200m, Day(-100).ToDateTime(TimeOnly.MinValue)),
                new AccountBalanceSnapshot(linked, "Revolut", "checking", "2222", "EUR", 500m, Day(5).ToDateTime(new TimeOnly(12, 0))),
            ]);
        // The held account is paid 200 EUR on day 8 (230 USD at the day-6 rate, the honest flow's savings).
        _transactions.Setup(t => t.GetTransactionsAsync(UserId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new BankingTransactionSummary(held, "truelayer", "credit", 200m, "EUR", 240m, Day(8).ToDateTime(new TimeOnly(9, 0)), false),
            ]);
        _bankLinks.Setup(r => r.GetChangesSinceAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AccountScopeChange("truelayer", AccountScopeChangeKinds.Linked, Day(5).ToDateTime(new TimeOnly(12, 0)))]);
        Setup(
            230m,
            Close(0, banking: 1_000m * 1.10m),
            Close(4, banking: 1_000m * 1.15m),
            Close(6, banking: (1_000m * 1.15m) + (500m * 1.15m)),
            Close(10, banking: (1_200m * 1.20m) + (500m * 1.20m)));

        var result = await Handle();

        // The link stretch (day 4 to 6) is the newly linked value; the rate moves either side of it are FX: the held
        // account's 1,000 then 1,200 EUR across two 0.05 moves, the linked one's 500 EUR only after it joined.
        result.NewlyLinked.Should().Be(575m);
        result.FxTranslation.Should().Be(135m);
        result.Savings.Should().Be(230m);
        result.Residual.Should().Be(0m);
        (Explained(result) + result.Residual).Should().Be(result.EndValue - result.StartValue);
        result.ResidualAboveTolerance.Should().BeFalse();
        result.Reasons.Should().Contain(r => r.Code == HeadlineReasonCode.ScopeChanged);
    }

    [Theory]
    [InlineData(true, 500, 0)]
    [InlineData(false, 0, 500)]
    public async Task ASleeveALinkFilledMidWindow_BringsItsFirstValueInAsNewlyLinked(bool linked, int newlyLinked, int residual)
    {
        // An empty brokerage gets its first close of 500 on day 5 and grows 100 by the closing. Linked that day, the 500
        // is newly linked; with no link it could be a first deposit, so it stays in the residual.
        if (linked)
        {
            _brokerageLinks.Setup(r => r.GetChangesSinceAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([new AccountScopeChange("ibkr", AccountScopeChangeKinds.Linked, Day(5).ToDateTime(new TimeOnly(8, 0)))]);
        }

        Setup(0m, Close(0, banking: 1_000m), Close(10, banking: 1_000m, brokerage: 600m));
        SetupSleeve(WealthSourceIds.Brokerage, Sleeve(100m) with { From = Day(5), StartValue = 500m, EndValue = 600m });

        var result = await Handle();

        result.InvestmentReturn.Should().Be(100m);
        result.NewlyLinked.Should().Be(newlyLinked);
        result.Residual.Should().Be(residual);
        (Explained(result) + result.Residual).Should().Be(result.EndValue - result.StartValue);
    }

    [Fact]
    public async Task FewerThanTwoCloses_LeaveTheBridgeUnavailable()
    {
        Setup(0m, Close(0, banking: 1_000m));

        var result = await Handle();

        result.StartValue.Should().BeNull();
        result.Residual.Should().BeNull();
        result.Quality.Should().Be(HeadlineQuality.Unavailable);
        result.Reasons.Should().ContainSingle(r => r.Code == HeadlineReasonCode.ShortHistory);
    }
}
