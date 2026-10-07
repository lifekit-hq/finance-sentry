namespace FinanceSentry.Tests.Unit.BrokerageSync;

using FinanceSentry.Modules.BrokerageSync.Application.Commands;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class DisconnectIBKRProviderTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static (DisconnectIBKRCommandHandler Handler, Mock<IBrokerageHoldingRepository> Holdings) Build(params BrokerageHolding[] rows)
    {
        var credentials = new Mock<IIBKRCredentialRepository>();
        credentials.Setup(c => c.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync((IBKRCredential?)null);
        var holdings = new Mock<IBrokerageHoldingRepository>();
        holdings.Setup(h => h.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(rows);
        return (new DisconnectIBKRCommandHandler(credentials.Object, holdings.Object), holdings);
    }

    [Fact]
    public async Task A_user_with_only_Inzhur_holdings_has_no_IBKR_account_to_disconnect()
    {
        var (handler, holdings) = Build(new BrokerageHolding(UserId, "Fund A", "REIT", 10m, 30m, "inzhur"));

        var act = () => handler.Handle(new DisconnectIBKRCommand(UserId), CancellationToken.None);

        await act.Should().ThrowAsync<BrokerAccountNotFoundException>();
        holdings.Verify(h => h.DeleteByUserIdAndProviderAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IBKR_holdings_are_removed_alone_when_Inzhur_rows_exist_too()
    {
        var (handler, holdings) = Build(
            new BrokerageHolding(UserId, "AAPL", "STK", 1m, 200m, "ibkr"),
            new BrokerageHolding(UserId, "Fund A", "REIT", 10m, 30m, "inzhur"));

        await handler.Handle(new DisconnectIBKRCommand(UserId), CancellationToken.None);

        holdings.Verify(h => h.DeleteByUserIdAndProviderAsync(UserId, "ibkr", It.IsAny<CancellationToken>()), Times.Once);
    }
}
