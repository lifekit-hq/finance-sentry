using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Ports;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Modules.Radar.Tests.Income;

public sealed class BookIncomeServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 11, 24);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
    }

    private sealed class Brokerage(params IncomeReceipt[] receipts) : IBrokerageIncomeReader
    {
        public Task<IReadOnlyList<IncomeReceipt>> ReadAsync(Guid userId, DateOnly from, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IncomeReceipt>>([.. receipts.Where(r => r.Date > from)]);
    }

    private sealed class Crypto(params IncomeReceipt[] receipts) : ICryptoIncomeReader
    {
        public Task<IReadOnlyList<IncomeReceipt>> ReadAsync(Guid userId, DateOnly from, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IncomeReceipt>>([.. receipts.Where(r => r.Date > from)]);
    }

    private sealed class Holdings(params BrokerageHoldingSummary[] rows) : IBrokerageHoldingsReader
    {
        public Task<IReadOnlyList<BrokerageHoldingSummary>> GetHoldingsAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BrokerageHoldingSummary>>(rows);
    }

    private sealed class Outlook(Dictionary<string, IncomeOutlook> byTicker) : IIncomeOutlookSource
    {
        public Task<IReadOnlyDictionary<string, IncomeOutlook>> GetAsync(
            IReadOnlyCollection<string> tickers, DateOnly from, DateOnly to, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, IncomeOutlook>>(byTicker);
    }

    private static BookIncomeService Sut(
        IBrokerageIncomeReader brokerage,
        ICryptoIncomeReader? crypto = null,
        IBrokerageHoldingsReader? holdings = null,
        Dictionary<string, IncomeOutlook>? outlook = null)
        => new(brokerage, crypto ?? new Crypto(), holdings ?? new Holdings(), new Outlook(outlook ?? []), new FixedClock());

    private static BrokerageHoldingSummary Position(string symbol, decimal value, decimal? cost, string basis = "Verified")
        => new(symbol, "Stock", 10m, value, DateTime.UtcNow, "ibkr", cost, basis);

    private static IncomeReceipt Dividend(string symbol, DateOnly date, decimal usd)
        => new(symbol, IncomeReceiptKind.Dividend, date, usd, false);

    private static IncomeReceipt Tax(string symbol, DateOnly date, decimal usd)
        => new(symbol, IncomeReceiptKind.Dividend, date, usd, true);

    [Fact]
    public async Task HoldingWithDividends_ReportsTrailingYearAndYearToDateWithForwardEstimateAndYieldOnCost()
    {
        var sut = Sut(
            new Brokerage(
                Dividend("KO", new DateOnly(2026, 3, 14), 30m),
                Dividend("KO", new DateOnly(2026, 9, 14), 30m),
                Dividend("KO", new DateOnly(2025, 12, 14), 25m),
                Dividend("KO", new DateOnly(2025, 6, 1), 99m)),
            holdings: new Holdings(Position("KO", 2000m, 1500m)),
            outlook: new() { ["KO"] = new IncomeOutlook(0.03m, new DateOnly(2026, 12, 12)) });

        var income = await sut.GetAsync(UserId);

        var ko = income.Holdings.Should().ContainSingle().Subject;
        ko.Symbol.Should().Be("KO");
        ko.Ttm.Net.Should().Be(85m, "the dividend from before the trailing year is out");
        ko.Ytd.Net.Should().Be(60m, "last December is before this calendar year");
        ko.ForwardAnnualUsd.Should().Be(60m);
        ko.YieldOnCost.Should().Be(Math.Round(85m / 1500m, 4));
        ko.NextExDividend.Should().Be(new DateOnly(2026, 12, 12));
        income.Ttm.Net.Should().Be(85m);
        income.Ytd.Net.Should().Be(60m);
        income.ForwardAnnualUsd.Should().Be(60m);
        income.YieldOnCost.Should().Be(Math.Round(85m / 1500m, 4));
    }

    [Fact]
    public async Task HoldingWithoutDividends_IsNotListedAndLeavesTheTotalsAlone()
    {
        var sut = Sut(
            new Brokerage(Dividend("KO", new DateOnly(2026, 3, 14), 30m)),
            holdings: new Holdings(Position("KO", 2000m, 1500m), Position("NVDA", 5000m, 2000m)),
            outlook: new() { ["KO"] = new IncomeOutlook(0.03m, null), ["NVDA"] = new IncomeOutlook(0m, null) });

        var income = await sut.GetAsync(UserId);

        income.Holdings.Select(h => h.Symbol).Should().Equal("KO");
        income.Ttm.Net.Should().Be(30m);
        income.ForwardAnnualUsd.Should().Be(60m, "a zero yield adds nothing");
    }

    [Fact]
    public async Task WithholdingTax_IsNettedAndShownBesideTheGross()
    {
        var sut = Sut(
            new Brokerage(
                Dividend("ASML", new DateOnly(2026, 5, 1), 100m),
                Tax("ASML", new DateOnly(2026, 5, 1), -15m),
                Dividend("ASML", new DateOnly(2026, 11, 1), 100m),
                Tax("ASML", new DateOnly(2026, 11, 1), -15m)),
            holdings: new Holdings(Position("ASML", 4000m, 2000m)));

        var income = await sut.GetAsync(UserId);

        income.Ttm.Should().Be(new IncomeWindow(200m, 30m, 170m));
        income.Holdings.Single().Ttm.Should().Be(new IncomeWindow(200m, 30m, 170m));
        income.Holdings.Single().YieldOnCost.Should().Be(0.085m, "yield on cost is on the net income");
    }

    [Fact]
    public async Task StakingRewards_JoinTheBookAndAnUnpricedRewardIsCountedNotZeroed()
    {
        var sut = Sut(
            new Brokerage(),
            new Crypto(
                new IncomeReceipt("ETH", IncomeReceiptKind.Staking, new DateOnly(2026, 10, 1), 12.5m, false),
                new IncomeReceipt("ETH", IncomeReceiptKind.Staking, new DateOnly(2026, 10, 2), null, false)));

        var income = await sut.GetAsync(UserId);

        income.Ttm.Net.Should().Be(12.5m);
        income.UnvaluedReceipts.Should().Be(1);
        income.Holdings.Single().Kinds.Should().Equal(IncomeReceiptKind.Staking);
        income.YieldOnCost.Should().BeNull("a coin has no verified brokerage cost basis");
    }

    [Fact]
    public async Task UnverifiedCostBasis_GivesNoYieldOnCost()
    {
        var sut = Sut(
            new Brokerage(Dividend("KO", new DateOnly(2026, 3, 14), 30m)),
            holdings: new Holdings(Position("KO", 2000m, null, "Unverified")));

        var income = await sut.GetAsync(UserId);

        income.Holdings.Single().YieldOnCost.Should().BeNull();
        income.YieldOnCost.Should().BeNull();
        income.ForwardAnnualUsd.Should().BeNull("no yield is stored for it");
    }

    [Fact]
    public async Task EmptyBook_IsZeroIncomeWithNoEstimate()
    {
        var income = await Sut(new Brokerage()).GetAsync(UserId);

        income.AsOf.Should().Be(Today);
        income.Ttm.Should().Be(IncomeWindow.Zero);
        income.Ytd.Should().Be(IncomeWindow.Zero);
        income.UnvaluedReceipts.Should().Be(0);
        income.Holdings.Should().BeEmpty();
        income.ForwardAnnualUsd.Should().BeNull();
        income.YieldOnCost.Should().BeNull();
    }
}
