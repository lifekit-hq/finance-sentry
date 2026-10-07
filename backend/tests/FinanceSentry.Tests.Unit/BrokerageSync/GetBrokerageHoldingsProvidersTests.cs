namespace FinanceSentry.Tests.Unit.BrokerageSync;

using FinanceSentry.Modules.BrokerageSync.Application.Queries;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class GetBrokerageHoldingsProvidersTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static async Task<BrokerageHoldingsResponse> QueryAsync(params BrokerageHolding[] rows)
    {
        var holdings = new Mock<IBrokerageHoldingRepository>();
        holdings.Setup(h => h.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(rows);
        var trades = new Mock<IBrokerageTradeRepository>();
        trades.Setup(t => t.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var handler = new GetBrokerageHoldingsQueryHandler(holdings.Object, trades.Object, new BrokerageCostBasisReconciler());
        return await handler.Handle(new GetBrokerageHoldingsQuery(UserId), CancellationToken.None);
    }

    [Fact]
    public async Task Each_position_names_its_broker_and_a_mix_is_reported_as_such()
    {
        var response = await QueryAsync(
            new BrokerageHolding(UserId, "AAPL", "STK", 1m, 200m, "ibkr"),
            new BrokerageHolding(UserId, "Fund A", "REIT", 10m, 30m, "inzhur", averageCostUsd: 2.5m));

        response.Provider.Should().Be("mixed");
        response.IsStale.Should().BeFalse();
        response.Positions.Select(p => (p.Symbol, p.Provider)).Should().BeEquivalentTo([("AAPL", "ibkr"), ("Fund A", "inzhur")]);
    }

    [Fact]
    public async Task One_broker_is_named_at_the_top_level()
        => (await QueryAsync(new BrokerageHolding(UserId, "Fund A", "REIT", 10m, 30m, "inzhur"))).Provider.Should().Be("inzhur");

    [Fact]
    public async Task Inzhur_cost_is_not_verified_by_IBKR_fills_so_no_gain_is_shown()
    {
        var position = (await QueryAsync(new BrokerageHolding(UserId, "Fund A", "REIT", 10m, 30m, "inzhur", averageCostUsd: 2.5m)))
            .Positions.Single();

        position.BasisState.Should().NotBe("Verified");
        position.AverageCostUsd.Should().BeNull();
    }
}
