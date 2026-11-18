namespace FinanceSentry.Tests.Unit.MoneySemantics;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Integration;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Moq;

/// <summary>
/// The blessed scenario S of issue #1470 (cases 13-17): checking 1,000 EUR; credit card owing 200 EUR (stored
/// positive); IBKR cash 300 USD plus positions 4,700 USD; crypto 1,500 USDT plus 500 USD of BTC. Built through the
/// production readers and registration: the real <see cref="BankingAccountsReader"/> converts the stored native
/// balances, the real <c>AddCrossModulePorts</c> book service aggregates them.
/// </summary>
internal static class ScenarioS
{
    public static readonly Guid UserId = Guid.Parse("13131313-1313-1313-1313-131313131313");

    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A stored bank account: the native balance in its own currency, a card's debt stored positive.</summary>
    public static BankAccount Bank(string type, string currency, decimal balance) =>
        new(UserId, Guid.NewGuid().ToString(), "Bank", type, "1234", "Owner", currency, UserId, "monobank")
        {
            CurrentBalance = balance,
            SyncStatus = "active",
        };

    public static BankingAccountsReader BankingReader(params BankAccount[] accounts)
    {
        var repo = new Mock<IBankAccountRepository>();
        repo.Setup(r => r.GetByUserIdUnscopedAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(accounts.ToList());
        var jobs = new Mock<ISyncJobRepository>();
        jobs.Setup(j => j.GetLastSuccessfulSyncTimesByUserUnscopedAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DateTime>());
        return new BankingAccountsReader(repo.Object, jobs.Object, Mock.Of<IClosedAccountsReader>(), new FakeTimeProvider(Now));
    }

    public static BankAccount[] BankOfS() => [Bank("checking", "EUR", 1_000m), Bank("credit", "EUR", 200m)];

    public static BrokerageHoldingSummary[] BrokerageOfS() =>
    [
        new("AAPL", "Stock", 10m, 4_700m, DateTime.UtcNow, "ibkr", 4_000m, "Verified"),
        new("USD", "CASH", 300m, 300m, DateTime.UtcNow, "ibkr", null, "Unknown"),
    ];

    public static CryptoHoldingSummary[] CryptoOfS() =>
    [
        new("USDT", 1_500m, 0m, 1_500m, DateTime.UtcNow, "binance"),
        new("BTC", 0.01m, 0m, 500m, DateTime.UtcNow, "binance"),
    ];

    /// <summary>The book figures of the given sources, through the production registration.</summary>
    public static async Task<BookFigures> BookAsync(
        BankingAccountsReader banking,
        IReadOnlyList<BrokerageHoldingSummary>? brokerage,
        IReadOnlyList<CryptoHoldingSummary> crypto)
    {
        // A null brokerage list is a brokerage reader that is down: the book reads stale.
        var brokerageReader = Readers(brokerage);
        var cryptoReader = CryptoReader(crypto);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IBankingAccountsReader>(_ => banking);
        services.AddScoped(_ => brokerageReader.Object);
        services.AddScoped(_ => cryptoReader.Object);
        services.AddCrossModulePorts();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IBookFiguresService>().ReadAsync(UserId);
    }

    /// <summary>The brokerage holdings reader the book and the snapshot job both read (null: the read fails).</summary>
    public static Mock<IBrokerageHoldingsReader> Readers(IReadOnlyList<BrokerageHoldingSummary>? brokerage)
    {
        var reader = new Mock<IBrokerageHoldingsReader>();
        var setup = reader.Setup(b => b.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>()));
        if (brokerage is null)
            setup.ThrowsAsync(new InvalidOperationException("down"));
        else
            setup.ReturnsAsync(brokerage);
        return reader;
    }

    public static Mock<ICryptoHoldingsReader> CryptoReader(IReadOnlyList<CryptoHoldingSummary> crypto)
    {
        var reader = new Mock<ICryptoHoldingsReader>();
        reader.Setup(c => c.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(crypto);
        return reader;
    }

    public static Task<BookFigures> BookOfSAsync() => BookAsync(BankingReader(BankOfS()), BrokerageOfS(), CryptoOfS());
}
