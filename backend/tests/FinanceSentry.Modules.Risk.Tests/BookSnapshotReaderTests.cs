using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Risk.Application.Services;
using FinanceSentry.Modules.Risk.Domain;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceSentry.Modules.Risk.Tests;

public sealed class BookSnapshotReaderTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private sealed class FakeBookFigures(BookFigures figures) : IBookFiguresService
    {
        public Task<BookFigures> ReadAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(figures);
    }

    private sealed class ThrowingBookFigures : IBookFiguresService
    {
        public Task<BookFigures> ReadAsync(Guid userId, CancellationToken ct = default)
            => throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task AllPositions_MappedToCorrectSleeve_WithWeightsComputed()
    {
        var figures = new BookFigures(
            CashUsd: 1_000m,
            BankingCashUsd: 1_000m,
            BrokerageCashUsd: 0m,
            InvestedValueUsd: 9_000m,
            TotalValueUsd: 10_000m,
            Positions:
            [
                new BookFigurePosition("BTC", AssetClassNormalizer.Crypto, 1m, null, 5_000m, "binance"),
                new BookFigurePosition("NVDA", AssetClassNormalizer.Equities, 10m, null, 4_000m, "ibkr"),
            ],
            IsStale: false,
            StaleSources: []);

        var reader = new BookSnapshotReader(new FakeBookFigures(figures), NullLogger<BookSnapshotReader>.Instance);
        var book = await reader.ReadAsync(UserId);

        book.IsStale.Should().BeFalse();
        book.TotalUsd.Should().Be(10_000m);
        book.CashUsd.Should().Be(1_000m);
        book.InvestedUsd.Should().Be(9_000m);
        book.Positions.Should().HaveCount(2);
        book.Positions.Should().ContainSingle(p => p.Symbol == "BTC" && p.Sleeve == RiskSleeve.Crypto);
        book.Positions.Should().ContainSingle(p => p.Symbol == "NVDA" && p.Sleeve == RiskSleeve.Brokerage);
        book.Positions.Single(p => p.Symbol == "BTC").WeightPct.Should().Be(5_000m / 10_000m);
        book.Positions.Single(p => p.Symbol == "NVDA").WeightPct.Should().Be(4_000m / 10_000m);
    }

    [Fact]
    public async Task SameAssetOnTwoVenues_MergedIntoOnePosition()
    {
        var figures = new BookFigures(
            CashUsd: 0m,
            BankingCashUsd: 0m,
            BrokerageCashUsd: 0m,
            InvestedValueUsd: 30_000m,
            TotalValueUsd: 30_000m,
            Positions:
            [
                new BookFigurePosition("BTC", AssetClassNormalizer.Crypto, 0.1m, null, 6_000m, "binance"),
                new BookFigurePosition("BTC", AssetClassNormalizer.Crypto, 0.3m, null, 18_000m, "revolut_x"),
                new BookFigurePosition("ETH", AssetClassNormalizer.Crypto, 2m, null, 6_000m, "revolut_x"),
            ],
            IsStale: false,
            StaleSources: []);

        var reader = new BookSnapshotReader(new FakeBookFigures(figures), NullLogger<BookSnapshotReader>.Instance);
        var book = await reader.ReadAsync(UserId);

        book.Positions.Should().HaveCount(2);
        var btc = book.Positions.Single(p => p.Symbol == "BTC");
        btc.Sleeve.Should().Be(RiskSleeve.Crypto);
        btc.Quantity.Should().Be(0.4m);
        btc.UsdValue.Should().Be(24_000m);
        btc.WeightPct.Should().Be(24_000m / 30_000m);
    }

    [Fact]
    public async Task StaleBookFigures_PropagateStalenessToSnapshot()
    {
        var figures = new BookFigures(
            CashUsd: 1_000m,
            BankingCashUsd: 1_000m,
            BrokerageCashUsd: 0m,
            InvestedValueUsd: 4_000m,
            TotalValueUsd: 5_000m,
            Positions:
            [
                new BookFigurePosition("NVDA", AssetClassNormalizer.Equities, 10m, null, 4_000m, "ibkr"),
            ],
            IsStale: true,
            StaleSources: ["crypto"]);

        var reader = new BookSnapshotReader(new FakeBookFigures(figures), NullLogger<BookSnapshotReader>.Instance);
        var book = await reader.ReadAsync(UserId);

        book.IsStale.Should().BeTrue();
        book.StaleSources.Should().Contain("crypto");
        book.Positions.Should().ContainSingle();
    }

    [Fact]
    public async Task BookFiguresThrows_ReturnsEmptyStaleSnapshot()
    {
        var reader = new BookSnapshotReader(new ThrowingBookFigures(), NullLogger<BookSnapshotReader>.Instance);
        var book = await reader.ReadAsync(UserId);

        book.IsStale.Should().BeTrue();
        book.StaleSources.Should().Contain("all");
        book.TotalUsd.Should().Be(0m);
        book.Positions.Should().BeEmpty();
    }
}
