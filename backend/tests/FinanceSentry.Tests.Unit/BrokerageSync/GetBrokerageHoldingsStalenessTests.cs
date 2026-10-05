namespace FinanceSentry.Tests.Unit.BrokerageSync;

using FinanceSentry.Modules.BrokerageSync.Application.Queries;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class GetBrokerageHoldingsStalenessTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static async Task<BrokerageHoldingsResponse> QueryAsync(DateOnly flexAsOf)
    {
        var holdings = new Mock<IBrokerageHoldingRepository>();
        holdings.Setup(h => h.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new BrokerageHolding(UserId, "ZZZQ", "STK", 10m, 500m, "ibkr", flexAsOfDate: flexAsOf)]);
        var trades = new Mock<IBrokerageTradeRepository>();
        trades.Setup(t => t.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var handler = new GetBrokerageHoldingsQueryHandler(holdings.Object, trades.Object, new BrokerageCostBasisReconciler());
        return await handler.Handle(new GetBrokerageHoldingsQuery(UserId), CancellationToken.None);
    }

    private static DateOnly DaysAgo(int days) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-days);

    [Fact]
    public async Task FlexHoldings_AsOfFourDaysAgo_AreNotStale()
    {
        var response = await QueryAsync(DaysAgo(4));

        response.IsStale.Should().BeFalse();
        response.FlexAsOfDate.Should().Be(DaysAgo(4));
    }

    [Fact]
    public async Task FlexHoldings_AsOfFiveDaysAgo_AreStale()
    {
        (await QueryAsync(DaysAgo(5))).IsStale.Should().BeTrue();
    }
}
