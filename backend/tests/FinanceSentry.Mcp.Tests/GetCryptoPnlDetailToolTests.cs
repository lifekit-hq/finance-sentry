using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.CryptoSync.Application.Queries;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

public sealed class GetCryptoPnlDetailToolTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IQueryHandler<GetCryptoPnlDetailQuery, CryptoPnlDetailResponse>> _handler = new();

    private GetCryptoPnlDetailTool CreateSut() =>
        new(_handler.Object, new FakeIdentityResolver(), NullLogger<GetCryptoPnlDetailTool>.Instance);

    [Fact]
    public async Task ExecuteAsync_ReturnsEmpty_WhenHandlerThrows()
    {
        _handler
            .Setup(h => h.Handle(It.IsAny<GetCryptoPnlDetailQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        var result = await CreateSut().ExecuteAsync(UserId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_MapsAssetsWithCostBasis()
    {
        _handler
            .Setup(h => h.Handle(It.IsAny<GetCryptoPnlDetailQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CryptoPnlDetailResponse(
                Provider: "binance",
                SyncedAt: DateTime.UtcNow,
                Items: [
                    new CryptoPnlAssetDto(
                        Asset: "BTC",
                        Quantity: 0.5m,
                        CurrentValueUsd: 25_000m,
                        CostBasisUsd: 18_000m,
                        AverageBuyPriceUsd: 36_000m,
                        UnrealizedPnlUsd: 7_000m,
                        UnrealizedPnlPercent: 38.89m,
                        RealizedPnlUsd: 500m,
                        LastTradeAt: new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc),
                        TradeCount: 3,
                        Provider: "binance"),
                ],
                TotalUnrealizedPnlUsd: 7_000m,
                TotalRealizedPnlUsd: 500m));

        var result = await CreateSut().ExecuteAsync(UserId);

        result.Should().HaveCount(1);
        var btc = result[0];
        btc.Asset.Should().Be("BTC");
        btc.Quantity.Should().Be(0.5m);
        btc.CurrentValueUsd.Should().Be(25_000m);
        btc.CostBasisUsd.Should().Be(18_000m);
        btc.UnrealizedPnlUsd.Should().Be(7_000m);
        btc.RealizedPnlUsd.Should().Be(500m);
        btc.TradeCount.Should().Be(3);
        btc.Provider.Should().Be("binance");
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNullsForCostBasis_WhenNoTradesYet()
    {
        _handler
            .Setup(h => h.Handle(It.IsAny<GetCryptoPnlDetailQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CryptoPnlDetailResponse(
                Provider: "binance",
                SyncedAt: DateTime.UtcNow,
                Items: [
                    new CryptoPnlAssetDto("DOGE", 1000m, 50m, null, null, null, null, null, null, 0, "binance"),
                ],
                TotalUnrealizedPnlUsd: 0m,
                TotalRealizedPnlUsd: 0m));

        var result = await CreateSut().ExecuteAsync(UserId);

        result.Should().HaveCount(1);
        result[0].CostBasisUsd.Should().BeNull();
        result[0].UnrealizedPnlUsd.Should().BeNull();
        result[0].TradeCount.Should().Be(0);
    }
}
