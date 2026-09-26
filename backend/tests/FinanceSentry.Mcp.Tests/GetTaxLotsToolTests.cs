using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.BrokerageSync.Application.Queries;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

public sealed class GetTaxLotsToolTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IQueryHandler<GetTaxLotsQuery, TaxLotsResponse>> _handler = new();

    private GetTaxLotsTool CreateSut() =>
        new(_handler.Object, new FakeIdentityResolver(), NullLogger<GetTaxLotsTool>.Instance);

    [Fact]
    public async Task ExecuteAsync_ReturnsEmpty_WhenHandlerThrows()
    {
        _handler
            .Setup(h => h.Handle(It.IsAny<GetTaxLotsQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await CreateSut().ExecuteAsync(UserId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_MapsLots_FullyPopulated()
    {
        var acq = new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _handler
            .Setup(h => h.Handle(It.IsAny<GetTaxLotsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaxLotsResponse(
                Provider: "ibkr",
                SyncedAt: DateTime.UtcNow,
                Items: [
                    new TaxLotDto(
                        Symbol: "AAPL",
                        InstrumentType: "STK",
                        Quantity: 10m,
                        CurrentValueUsd: 1_900m,
                        AverageCostUsd: 150m,
                        CostBasisUsd: 1_500m,
                        UnrealizedPnlUsd: 400m,
                        UnrealizedPnlPercent: 26.67m,
                        AcquiredAt: acq,
                        IsLongTerm: true,
                        BasisState: "Verified"),
                ],
                TotalCostBasisUsd: 1_500m,
                TotalUnrealizedPnlUsd: 400m));

        var result = await CreateSut().ExecuteAsync(UserId);

        result.Should().HaveCount(1);
        var aapl = result[0];
        aapl.Symbol.Should().Be("AAPL");
        aapl.Quantity.Should().Be(10m);
        aapl.AverageCostUsd.Should().Be(150m);
        aapl.CostBasisUsd.Should().Be(1_500m);
        aapl.UnrealizedPnlUsd.Should().Be(400m);
        aapl.IsLongTerm.Should().BeTrue();
        aapl.AcquiredAt.Should().Be(acq);
        aapl.Provider.Should().Be("ibkr");
        aapl.BasisState.Should().Be("Verified");
    }

    [Fact]
    public async Task ExecuteAsync_PassesThroughNullsForUnknownCostBasis()
    {
        _handler
            .Setup(h => h.Handle(It.IsAny<GetTaxLotsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaxLotsResponse(
                Provider: "ibkr",
                SyncedAt: DateTime.UtcNow,
                Items: [
                    new TaxLotDto("SPY", "STK", 5m, 2_500m, null, null, null, null, null, false, "Unknown"),
                ],
                TotalCostBasisUsd: 0m,
                TotalUnrealizedPnlUsd: 0m));

        var result = await CreateSut().ExecuteAsync(UserId);

        result.Should().HaveCount(1);
        result[0].AverageCostUsd.Should().BeNull();
        result[0].CostBasisUsd.Should().BeNull();
        result[0].UnrealizedPnlUsd.Should().BeNull();
        result[0].IsLongTerm.Should().BeFalse();
    }
}
