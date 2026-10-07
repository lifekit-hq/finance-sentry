namespace FinanceSentry.Tests.Unit.BrokerageSync;

using FinanceSentry.Modules.BrokerageSync.Application.Queries;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class GetTaxLotsProviderTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static async Task<TaxLotsResponse> QueryAsync(params BrokerageHolding[] rows)
    {
        var holdings = new Mock<IBrokerageHoldingRepository>();
        holdings.Setup(h => h.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(rows);
        var trades = new Mock<IBrokerageTradeRepository>();
        trades.Setup(t => t.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var handler = new GetTaxLotsQueryHandler(holdings.Object, trades.Object, new BrokerageCostBasisReconciler());
        return await handler.Handle(new GetTaxLotsQuery(UserId), CancellationToken.None);
    }

    [Fact]
    public async Task Inzhur_rows_are_not_tax_lots()
    {
        var response = await QueryAsync(
            new BrokerageHolding(UserId, "AAPL", "STK", 1m, 200m, "ibkr"),
            new BrokerageHolding(UserId, "Fund A", "REIT", 10m, 30m, "inzhur", averageCostUsd: 2.5m, acquiredAt: DateTime.UtcNow.AddDays(-400)),
            new BrokerageHolding(UserId, "UAH Cash", "CASH", 100m, 2.4m, "inzhur"));

        response.Items.Select(i => i.Symbol).Should().Equal("AAPL");
    }

    [Fact]
    public async Task A_user_with_only_Inzhur_rows_has_no_tax_lots()
    {
        var response = await QueryAsync(new BrokerageHolding(UserId, "Fund A", "REIT", 10m, 30m, "inzhur"));

        response.Items.Should().BeEmpty();
        response.SyncedAt.Should().BeNull();
    }
}
