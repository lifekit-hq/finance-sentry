namespace FinanceSentry.Tests.Unit.BrokerageSync;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BrokerageSync.Application.Queries;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class GetBrokerageHoldingsVisibilityTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static async Task<BrokerageHoldingsResponse> QueryAsync(UserAssetVisibility? prefs, params BrokerageHolding[] rows)
    {
        var holdings = new Mock<IBrokerageHoldingRepository>();
        holdings.Setup(h => h.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(rows);
        var trades = new Mock<IBrokerageTradeRepository>();
        trades.Setup(t => t.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var visibility = new Mock<IUserAssetVisibilityReader>();
        visibility.Setup(v => v.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(prefs);
        var currency = new Mock<IUserBaseCurrencyReader>();
        currency.Setup(c => c.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync("USD");

        var handler = new GetBrokerageHoldingsQueryHandler(
            holdings.Object, trades.Object, new BrokerageCostBasisReconciler(), visibility.Object, currency.Object);
        return await handler.Handle(new GetBrokerageHoldingsQuery(UserId), CancellationToken.None);
    }

    private static readonly UserAssetVisibility HideSmall = new(true, true, 5m);

    [Fact]
    public async Task A_position_under_the_threshold_is_flagged_small_and_still_counts_in_the_total()
    {
        var response = await QueryAsync(HideSmall,
            new BrokerageHolding(UserId, "AAPL", "STK", 10m, 1500m, "ibkr"),
            new BrokerageHolding(UserId, "DUST", "STK", 2m, 3m, "ibkr"));

        response.Positions.Select(p => (p.Symbol, p.HiddenReason)).Should().BeEquivalentTo(
            [("AAPL", (string?)null), ("DUST", "small")]);
        response.TotalUsdValue.Should().Be(1503m, "hidden rows are labelled, never dropped from the sum the weights use");
        response.Positions.Sum(p => p.UsdValue).Should().Be(response.TotalUsdValue);
    }

    [Fact]
    public async Task An_unpriced_position_and_a_short_are_never_hidden()
    {
        var response = await QueryAsync(HideSmall,
            new BrokerageHolding(UserId, "NOPRICE", "STK", 4m, 0m, "ibkr"),
            new BrokerageHolding(UserId, "SHORT", "STK", -1m, -2m, "ibkr"));

        response.Positions.Should().OnlyContain(p => p.HiddenReason == null);
    }

    [Fact]
    public async Task Nothing_is_flagged_while_small_hiding_is_off()
    {
        var response = await QueryAsync(new UserAssetVisibility(true, false, 5m),
            new BrokerageHolding(UserId, "DUST", "STK", 2m, 3m, "ibkr"));

        response.Positions.Single().HiddenReason.Should().BeNull();
    }

    [Fact]
    public async Task Without_stored_preferences_nothing_small_is_hidden()
    {
        var response = await QueryAsync(null, new BrokerageHolding(UserId, "DUST", "STK", 2m, 0.5m, "ibkr"));

        response.Positions.Single().HiddenReason.Should().BeNull();
    }
}
