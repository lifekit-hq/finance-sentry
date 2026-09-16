namespace FinanceSentry.Tests.Unit.BankSync.Wealth;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Wealth.Application.Services;
using FluentAssertions;
using Moq;
using Xunit;

public class WealthAggregationServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static BankingAccountSummary MakeAccount(string provider, string currency, decimal? balance, DateTime? lastSuccessfulSync = null, string accountType = "checking")
        => new(Guid.NewGuid(), "Test Bank", accountType, "1234", provider, currency,
               balance, balance.HasValue ? CurrencyConverter.ToUsd(balance.Value, currency) : null, "synced",
               DateTime.UtcNow, lastSuccessfulSync ?? DateTime.UtcNow);

    private static BankingTransactionSummary MakeTx(Guid accountId, string provider, decimal amount, string type, DateTime date, bool isPending = false, string currency = "USD")
        => new(accountId, provider, type, amount, currency, CurrencyConverter.ToUsd(amount, currency), date, isPending);

    private static BankingAccountSummary MakeMonobankAccount(string currency, decimal? balance, string? productType, Guid credId)
        => new(Guid.NewGuid(), "Monobank", "checking", "1234", "monobank", currency,
               balance, balance.HasValue ? CurrencyConverter.ToUsd(balance.Value, currency) : null, "synced",
               DateTime.UtcNow, DateTime.UtcNow, MonobankCredentialId: credId, ProductType: productType);

    [Fact]
    public async Task GetWealthSummary_Monobank_GroupsByCard_HidesEmptySubAccountsAndEmptyCards()
    {
        var cred = Guid.NewGuid();
        var accounts = new[]
        {
            MakeMonobankAccount("UAH", 10000m, "black", cred),  // black card — has money
            MakeMonobankAccount("USD", 0m, "black", cred),      // black card — empty currency, hidden
            MakeMonobankAccount("UAH", 5000m, "white", cred),   // white card — has money
            MakeMonobankAccount("UAH", 0m, "platinum", cred),   // platinum — all empty, whole card hidden
        };

        var svc = BuildService(accounts);
        var result = await svc.GetWealthSummaryAsync(UserId, null, null);

        var inst = result.Categories.Single(c => c.Category == "banking").Institutions.Single();
        inst.Cards.Should().NotBeNull();
        inst.Cards!.Select(c => c.CardType).Should().BeEquivalentTo(["black", "white"]); // platinum dropped

        var black = inst.Cards!.Single(c => c.CardType == "black");
        black.DisplayName.Should().Be("Black");
        black.Accounts.Should().HaveCount(1);                 // empty USD hidden
        black.Accounts.Single().Currency.Should().Be("UAH");
    }

    [Fact]
    public async Task GetWealthSummary_NonMonobank_HasNoCardGrouping()
    {
        var svc = BuildService([MakeAccount("truelayer", "USD", 1000m)]);
        var result = await svc.GetWealthSummaryAsync(UserId, null, null);

        result.Categories.Single().Institutions.Single().Cards.Should().BeNull();
    }

    private WealthAggregationService BuildService(
        IEnumerable<BankingAccountSummary> accounts,
        IEnumerable<BankingTransactionSummary>? transactions = null)
    {
        var accountsMock = new Mock<IBankingAccountsReader>();
        accountsMock.Setup(r => r.GetAccountSummariesAsync(UserId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(accounts.ToList());

        var txMock = new Mock<IBankingTransactionReader>();
        txMock.Setup(r => r.GetTransactionsAsync(UserId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((transactions ?? []).ToList());

        return new WealthAggregationService(accountsMock.Object, txMock.Object);
    }

    // ── GetWealthSummaryAsync ─────────────────────────────────────────────────

    private static WealthAggregationService BuildServiceWithCrypto(params CryptoHoldingSummary[] holdings)
    {
        var accountsMock = new Mock<IBankingAccountsReader>();
        accountsMock.Setup(r => r.GetAccountSummariesAsync(UserId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync([]);
        var cryptoMock = new Mock<ICryptoHoldingsReader>();
        cryptoMock.Setup(r => r.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(holdings);

        return new WealthAggregationService(accountsMock.Object, Mock.Of<IBankingTransactionReader>(), cryptoMock.Object);
    }

    private static CryptoHoldingSummary Holding(string provider, string asset, decimal quantity, decimal usdValue)
        => new(asset, quantity, 0m, usdValue, DateTime.UtcNow, provider);

    [Fact]
    public async Task GetWealthSummary_TwoCryptoVenues_AreTwoInstitutions_EachWithItsOwnAssets()
    {
        var svc = BuildServiceWithCrypto(
            Holding("binance", "BTC", 0.5m, 30_000m),
            Holding("revolut_x", "BTC", 0.1m, 6_000m),
            Holding("revolut_x", "ETH", 2m, 6_000m));

        var result = await svc.GetWealthSummaryAsync(UserId, null, null);

        var crypto = result.Categories.Single(c => c.Category == "crypto");
        crypto.TotalInBaseCurrency.Should().Be(42_000m);
        crypto.Institutions.Select(i => (i.Provider, i.Name, i.TotalInBaseCurrency)).Should().BeEquivalentTo(
            [("binance", "Binance", 30_000m), ("revolut_x", "Revolut X", 12_000m)]);
        var revolutX = crypto.Institutions.Single(i => i.Provider == "revolut_x");
        revolutX.Accounts.Select(a => (a.BankName, a.Provider, a.Currency)).Should().BeEquivalentTo(
            [("Revolut X", "revolut_x", "BTC"), ("Revolut X", "revolut_x", "ETH")]);
        result.Categories.Should().NotContain(c => c.Category == "banking",
            "venue holdings are never counted as bank cash");
    }

    [Theory]
    [InlineData("revolut_x", 6_000)]
    [InlineData("binance", 30_000)]
    public async Task GetWealthSummary_CryptoProviderFilter_KeepsOnlyThatVenue(string provider, decimal expected)
    {
        var svc = BuildServiceWithCrypto(
            Holding("binance", "BTC", 0.5m, 30_000m),
            Holding("revolut_x", "BTC", 0.1m, 6_000m));

        var result = await svc.GetWealthSummaryAsync(UserId, null, provider);

        result.Categories.Single().Institutions.Single().Provider.Should().Be(provider);
        result.TotalNetWorth.Should().Be(expected);
    }

    [Fact]
    public async Task GetWealthSummary_NonCryptoProviderFilter_SkipsCrypto()
    {
        var svc = BuildServiceWithCrypto(Holding("revolut_x", "BTC", 0.1m, 6_000m));

        var result = await svc.GetWealthSummaryAsync(UserId, null, "ibkr");

        result.Categories.Should().BeEmpty();
    }

    [Fact]
    public async Task GetWealthSummary_MixedCurrencies_CorrectUsdTotal()
    {
        var accounts = new[]
        {
            MakeAccount("truelayer", "USD", 1000m),
            MakeAccount("monobank", "UAH", 100000m),
        };

        var svc = BuildService(accounts);
        var result = await svc.GetWealthSummaryAsync(UserId, null, null);

        result.TotalNetWorth.Should().Be(3400m);
        result.BaseCurrency.Should().Be("USD");
    }

    [Fact]
    public async Task GetWealthSummary_BankingNotSyncedWithinWindow_MarkedStale()
    {
        // A bank account that looks synced but hasn't had a successful sync in >36h is stale —
        // so the frozen balance shows as stale rather than current (Revolut/AIB lapse case).
        var accounts = new[]
        {
            MakeAccount("truelayer", "USD", 1000m, lastSuccessfulSync: DateTime.UtcNow.AddDays(-4)),
        };

        var svc = BuildService(accounts);
        var result = await svc.GetWealthSummaryAsync(UserId, null, null);

        result.Categories.Single().Institutions.Single().SyncStatus.Should().Be("stale");
    }

    [Fact]
    public async Task GetWealthSummary_BankingSyncedRecently_NotStale()
    {
        var accounts = new[] { MakeAccount("truelayer", "USD", 1000m, lastSuccessfulSync: DateTime.UtcNow.AddHours(-2)) };

        var svc = BuildService(accounts);
        var result = await svc.GetWealthSummaryAsync(UserId, null, null);

        result.Categories.Single().Institutions.Single().SyncStatus.Should().Be("synced");
    }

    [Fact]
    public async Task GetWealthSummary_NullBalanceAccount_ExcludedFromTotal_IncludedInList()
    {
        var accounts = new[]
        {
            MakeAccount("truelayer", "USD", 500m),
            new BankingAccountSummary(Guid.NewGuid(), "Test Bank", "checking", "1234", "truelayer", "USD", null, null, "synced", null),
        };

        var svc = BuildService(accounts);
        var result = await svc.GetWealthSummaryAsync(UserId, null, null);

        result.TotalNetWorth.Should().Be(500m);
        result.Categories.Single().Institutions.SelectMany(i => i.Accounts).Should().HaveCount(2);
        result.Categories.Single().Institutions.SelectMany(i => i.Accounts).First(a => a.CurrentBalance is null).BalanceInBaseCurrency.Should().BeNull();
    }

    [Fact]
    public async Task GetWealthSummary_CreditAccount_NegatedInTotal_RawInAccountList()
    {
        // #498 — a credit card balance is the amount owed: it subtracts from the
        // institution/net-worth totals but the per-account row keeps the raw value
        var accounts = new[]
        {
            MakeAccount("truelayer", "USD", 1000m),
            MakeAccount("truelayer", "USD", 300m, accountType: "credit"),
        };

        var svc = BuildService(accounts);
        var result = await svc.GetWealthSummaryAsync(UserId, null, null);

        result.TotalNetWorth.Should().Be(700m);
        var creditAccount = result.Categories.Single().Institutions
            .SelectMany(i => i.Accounts).Single(a => a.AccountType == "credit");
        creditAccount.BalanceInBaseCurrency.Should().Be(300m);
    }

    [Fact]
    public async Task GetWealthSummary_EmptyAccountList_ReturnsZeroTotal()
    {
        var svc = BuildService([]);
        var result = await svc.GetWealthSummaryAsync(UserId, null, null);

        result.TotalNetWorth.Should().Be(0m);
        result.Categories.Should().BeEmpty();
    }

    [Fact]
    public async Task GetWealthSummary_CategoryFilter_ExcludesNonMatching()
    {
        var accounts = new[]
        {
            MakeAccount("truelayer", "USD", 1000m),
            MakeAccount("binance", "USD", 500m),
        };

        var svc = BuildService(accounts);
        var result = await svc.GetWealthSummaryAsync(UserId, "banking", null);

        result.Categories.Should().HaveCount(1);
        result.Categories.Single().Category.Should().Be("banking");
        result.TotalNetWorth.Should().Be(1000m);
    }

    [Fact]
    public async Task GetWealthSummary_ProviderFilter_TakesPrecedenceOverCategory()
    {
        var accounts = new[]
        {
            MakeAccount("truelayer", "USD", 1000m),
            MakeAccount("monobank", "USD", 500m),
        };

        var svc = BuildService(accounts);
        var result = await svc.GetWealthSummaryAsync(UserId, "banking", "monobank");

        result.TotalNetWorth.Should().Be(500m);
        result.Categories.Single().Institutions.SelectMany(i => i.Accounts).Single().Provider.Should().Be("monobank");
    }

    [Fact]
    public async Task GetWealthSummary_UnknownProvider_ReturnsEmpty()
    {
        var accounts = new[] { MakeAccount("truelayer", "USD", 1000m) };

        var svc = BuildService(accounts);
        var result = await svc.GetWealthSummaryAsync(UserId, null, "nonexistent_bank");

        result.TotalNetWorth.Should().Be(0m);
        result.Categories.Should().BeEmpty();
    }

    [Fact]
    public async Task GetWealthSummary_InvalidCategory_Throws()
    {
        var svc = BuildService([]);
        await svc.Invoking(s => s.GetWealthSummaryAsync(UserId, "invalid_cat", null))
                 .Should().ThrowAsync<ArgumentException>();
    }

    // ── GetTransactionSummaryAsync ────────────────────────────────────────────

    [Fact]
    public async Task GetTransactionSummary_DebitCreditSplit_Correct()
    {
        var accountId = Guid.NewGuid();
        var date = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc);
        var txs = new[]
        {
            MakeTx(accountId, "truelayer", 100m, "debit", date),
            MakeTx(accountId, "truelayer", 200m, "debit", date),
            MakeTx(accountId, "truelayer", 300m, "credit", date),
        };

        var svc = BuildService([MakeAccount("truelayer", "USD", 1000m)], txs);
        var result = await svc.GetTransactionSummaryAsync(UserId, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), null, null);

        result.TotalDebits.Should().Be(300m);
        result.TotalCredits.Should().Be(300m);
        result.NetFlow.Should().Be(0m);
    }

    [Fact]
    public async Task GetTransactionSummary_MixedCurrencies_SumsInUsdNotNativeMagnitudes()
    {
        // Regression: previously summed native Amount, so ₴10,000 + $100 read as $10,100.
        // Correct is $100 + (10,000 × 0.024 UAH) = $340.
        var usdAcct = Guid.NewGuid();
        var uahAcct = Guid.NewGuid();
        var date = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc);
        var txs = new[]
        {
            MakeTx(usdAcct, "truelayer", 100m, "debit", date),
            MakeTx(uahAcct, "monobank", 10000m, "debit", date, currency: "UAH"),
        };

        var svc = BuildService([MakeAccount("truelayer", "USD", 1000m), MakeAccount("monobank", "UAH", 50000m)], txs);
        var result = await svc.GetTransactionSummaryAsync(UserId, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), null, null);

        result.TotalDebits.Should().Be(340m);
    }

    [Fact]
    public async Task GetTransactionSummary_EmptyWindow_ReturnsZeros()
    {
        var svc = BuildService([MakeAccount("truelayer", "USD", 1000m)], []);
        var result = await svc.GetTransactionSummaryAsync(UserId, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), null, null);

        result.TotalDebits.Should().Be(0m);
        result.TotalCredits.Should().Be(0m);
        result.Categories.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTransactionSummary_ProviderFilter_ScopesTransactions()
    {
        var date = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc);
        var txs = new[]
        {
            MakeTx(Guid.NewGuid(), "truelayer", 100m, "debit", date),
            MakeTx(Guid.NewGuid(), "monobank", 200m, "debit", date),
        };

        var svc = BuildService([MakeAccount("truelayer", "USD", 1000m), MakeAccount("monobank", "UAH", 50000m)], txs);
        var result = await svc.GetTransactionSummaryAsync(UserId, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), null, "monobank");

        result.TotalDebits.Should().Be(200m);
    }

    [Fact]
    public async Task GetTransactionSummary_PendingTransactions_Excluded()
    {
        var accountId = Guid.NewGuid();
        var date = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc);
        var txs = new[]
        {
            MakeTx(accountId, "truelayer", 999m, "debit", date, isPending: true),
            MakeTx(accountId, "truelayer", 50m, "debit", date, isPending: false),
        };

        var svc = BuildService([MakeAccount("truelayer", "USD", 1000m)], txs);
        var result = await svc.GetTransactionSummaryAsync(UserId, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), null, null);

        result.TotalDebits.Should().Be(50m);
    }

    [Fact]
    public async Task GetTransactionSummary_CategoryGrouping_Correct()
    {
        var date = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc);
        var txs = new[]
        {
            MakeTx(Guid.NewGuid(), "truelayer", 100m, "debit", date),
            MakeTx(Guid.NewGuid(), "binance", 50m, "debit", date),
        };

        var svc = BuildService([MakeAccount("truelayer", "USD", 1000m), MakeAccount("binance", "USD", 500m)], txs);
        var result = await svc.GetTransactionSummaryAsync(UserId, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), null, null);

        result.Categories.Should().HaveCount(2);
        result.Categories.First(c => c.Category == "banking").TotalDebits.Should().Be(100m);
        result.Categories.First(c => c.Category == "crypto").TotalDebits.Should().Be(50m);
    }
}
