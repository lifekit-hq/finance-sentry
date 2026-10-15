namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Services;
using FinanceSentry.Core.Utils;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// R8, PR 1: a source registered in DI is summed by <see cref="BookFiguresService"/> without changing it.
/// </summary>
public sealed class BookFiguresWealthSourceTests
{
    private static readonly Guid UserId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    [Fact]
    public async Task AFourthSourceRegisteredInDi_IsSummedIntoTheBook()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBookFigures();
        services.AddScoped<IBankingAccountsReader>(_ => new FixedBanking());
        services.AddScoped<IBrokerageHoldingsReader>(_ => new FixedBrokerage());
        services.AddScoped<ICryptoHoldingsReader>(_ => new FixedCrypto());
        services.AddScoped<IWealthSource>(_ => new FakeSource(
            "marketplace",
            EconomyKind.Marketplace,
            new WealthSourceReading(
                CashUsd: 75m,
                Positions: [new BookFigurePosition("ITEM-1", "Collectible", 2m, null, 300m, "marketplace")],
                FxStatus: FxStatus.TodayRate)));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var book = await scope.ServiceProvider.GetRequiredService<IBookFiguresService>().ReadAsync(UserId);

        book.CashUsd.Should().Be(75m);
        book.InvestedValueUsd.Should().Be(300m);
        book.TotalValueUsd.Should().Be(375m);
        book.Positions.Should().ContainSingle().Which.Symbol.Should().Be("ITEM-1");
        book.FxStatus.Should().Be(FxStatus.TodayRate);
        book.IsStale.Should().BeFalse();

        // A kind with no legacy slice counts in the cash total only.
        book.BankingCashUsd.Should().Be(0m);
        book.BrokerageCashUsd.Should().Be(0m);
        book.VenueCashUsd.Should().Be(0m);
    }

    [Fact]
    public void AddBookFigures_CalledTwice_RegistersEachSourceOnce()
    {
        var services = new ServiceCollection();
        services.AddBookFigures();
        services.AddBookFigures();

        services.Count(d => d.ServiceType == typeof(IWealthSource)).Should().Be(3);
        services.Count(d => d.ServiceType == typeof(IBookFiguresService)).Should().Be(1);
    }

    [Fact]
    public async Task AFailingFourthSource_IsNamedStale_AndTheOthersStillCount()
    {
        var book = await new BookFiguresService(
            [
                new FakeSource("marketplace", EconomyKind.Marketplace, new WealthSourceReading(10m, [])),
                new FakeSource("steam", EconomyKind.Marketplace, null),
            ],
            NullLogger<BookFiguresService>.Instance).ReadAsync(UserId);

        book.TotalValueUsd.Should().Be(10m);
        book.IsStale.Should().BeTrue();
        book.StaleSources.Should().Equal("steam");
    }

    private sealed class FakeSource(string id, EconomyKind kind, WealthSourceReading? reading) : IWealthSource
    {
        public string SourceId => id;

        public EconomyKind Kind => kind;

        public Task<WealthSourceReading> ReadAsync(Guid userId, CancellationToken ct = default) =>
            reading is null ? throw new InvalidOperationException("unavailable") : Task.FromResult(reading);
    }

    private sealed class FixedBanking : IBankingAccountsReader
    {
        public Task<IReadOnlyList<BankingAccountSummary>> GetAccountSummariesAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BankingAccountSummary>>([]);

        public Task<IReadOnlyList<Guid>> GetAllActiveUserIdsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<IReadOnlyList<AccountBalanceSnapshot>> GetActiveAccountSnapshotsAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AccountBalanceSnapshot>>([]);
    }

    private sealed class FixedBrokerage : IBrokerageHoldingsReader
    {
        public Task<IReadOnlyList<BrokerageHoldingSummary>> GetHoldingsAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BrokerageHoldingSummary>>([]);
    }

    private sealed class FixedCrypto : ICryptoHoldingsReader
    {
        public Task<IReadOnlyList<CryptoHoldingSummary>> GetHoldingsAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CryptoHoldingSummary>>([]);
    }
}
