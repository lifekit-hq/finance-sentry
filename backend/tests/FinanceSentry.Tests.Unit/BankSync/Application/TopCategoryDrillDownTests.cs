namespace FinanceSentry.Tests.Unit.BankSync.Application;

using FinanceSentry.Core.Api;
using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.Application.Queries;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// The dashboard's Top spendings rows drill into the ledger as <c>category=KEY&amp;type=debit&amp;from=…</c>.
/// Every category the statistics can show must come back from the ledger query with exactly the
/// transactions it counted — including the computed <c>FAMILY_SUPPORT</c> bucket (never stored on a
/// row) and <c>UNCATEGORIZED</c>, which the statistics also use for a null category column. Both
/// sides run over the real repositories and services against one in-memory book. The ledger may
/// list more than the row counted (see <see cref="Ledger_AlsoListsCounterpartyClaimedDebits_OnTheirStoredCategory"/>).
/// </summary>
public class TopCategoryDrillDownTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private sealed record Book(BankSyncDbContext Context, Guid FamilyDebitId);

    private static async Task<Book> SeedAsync(Action<BankSyncDbContext, BankAccount>? extra = null)
    {
        var context = new BankSyncDbContext(
            new DbContextOptionsBuilder<BankSyncDbContext>()
                .UseInMemoryDatabase($"drilldown-{Guid.NewGuid():N}").Options,
            new FixedCurrentUser(UserId));

        var uah = Account("Monobank", "UAH");
        var eur = Account("Revolut", "EUR");
        var usd = Account("Interactive Brokers", "USD");

        var mom = new Counterparty { UserId = UserId, Name = "Mom", FlowRole = FlowRoles.FamilySupport };
        mom.Rules.Add(new CounterpartyRule { CounterpartyId = mom.Id, MatchType = "description_contains", Pattern = "To Mom" });
        mom.Rules.Add(new CounterpartyRule { CounterpartyId = mom.Id, MatchType = "description_contains", Pattern = "From Mom" });

        var familyDebit = Tx(uah, 2000m, "debit", "To Mom card transfer", CategoryKeys.TransferOut);
        var deleted = Tx(eur, 90m, "debit", "Duplicate dinner", "FOOD_AND_DRINK");
        deleted.SoftDelete();

        context.BankAccounts.AddRange(uah, eur, usd);
        context.Counterparties.Add(mom);
        context.Transactions.AddRange(
            Tx(uah, 500m, "debit", "Silpo", "FOOD_AND_DRINK"),
            Tx(eur, 20m, "debit", "Dinner", "FOOD_AND_DRINK"),
            Tx(eur, 5m, "credit", "Dinner refund", "FOOD_AND_DRINK"),
            Tx(uah, 300m, "debit", "Card payment without a category", category: null),
            Tx(usd, 7m, "debit", "IBKR market data fee", CategoryKeys.Uncategorized),
            Tx(eur, 40m, "debit", "Bolt", "TRANSPORTATION"),
            familyDebit,
            Tx(uah, 1000m, "credit", "From Mom rent", CategoryKeys.TransferIn),
            Tx(uah, 800m, "debit", "Top-up to own card", CategoryKeys.TransferOut),
            deleted);
        extra?.Invoke(context, uah);
        await context.SaveChangesAsync();

        return new Book(context, familyDebit.Id);
    }

    [Fact]
    public async Task EveryTopCategory_DrillsIntoTheTransactionsItCounted()
    {
        var book = await SeedAsync();
        var from = MonthWindow.StartOfMonthsAgo(2);
        var transactions = new TransactionRepository(book.Context);
        var accounts = new BankAccountRepository(book.Context);
        var classification = new CounterpartyClassificationService(
            new CounterpartyRepository(book.Context), transactions, accounts);

        var topCategories = await new MerchantCategoryStatisticsService(transactions, accounts, new TransferDetectionService())
            .GetTopCategoriesAsync(
                UserId, await classification.ClassifyForWindowAsync(UserId, 2), limit: 10, months: 2, from: from);
        var ledger = new GetAllTransactionsQueryHandler(transactions, accounts, classification);

        topCategories.Select(c => c.Category).Should().BeEquivalentTo(
            ["FOOD_AND_DRINK", "TRANSPORTATION", CategoryKeys.Uncategorized, CategoryKeys.FamilySupport]);
        foreach (var category in topCategories)
        {
            var page = await ledger.Handle(
                new GetAllTransactionsQuery(
                    UserId, new PagedRequest(0, 100), From: from, TransactionType: "debit",
                    Categories: [category.Category]),
                CancellationToken.None);

            page.Transactions.Sum(t => t.AmountUsd).Should().Be(
                category.TotalSpend, $"the ledger drill-down for {category.Category} lists what Top spendings counted");
        }
    }

    [Fact]
    public async Task FamilySupport_ResolvesToTheOutboundCounterpartyLegs()
    {
        var book = await SeedAsync();
        var transactions = new TransactionRepository(book.Context);
        var accounts = new BankAccountRepository(book.Context);
        var ledger = new GetAllTransactionsQueryHandler(
            transactions, accounts,
            new CounterpartyClassificationService(new CounterpartyRepository(book.Context), transactions, accounts));

        var page = await ledger.Handle(
            new GetAllTransactionsQuery(UserId, new PagedRequest(), Categories: [CategoryKeys.FamilySupport]),
            CancellationToken.None);

        page.Transactions.Select(t => t.TransactionId).Should().Equal(book.FamilyDebitId);
    }

    [Fact]
    public async Task Uncategorized_MatchesANullCategoryColumn()
    {
        var book = await SeedAsync();
        var transactions = new TransactionRepository(book.Context);
        var accounts = new BankAccountRepository(book.Context);
        var ledger = new GetAllTransactionsQueryHandler(
            transactions, accounts,
            new CounterpartyClassificationService(new CounterpartyRepository(book.Context), transactions, accounts));

        var page = await ledger.Handle(
            new GetAllTransactionsQuery(UserId, new PagedRequest(), Categories: [CategoryKeys.Uncategorized]),
            CancellationToken.None);

        page.Transactions.Select(t => t.Description).Should().BeEquivalentTo(
            ["Card payment without a category", "IBKR market data fee"]);
    }

    [Fact]
    public async Task Ledger_AlsoListsCounterpartyClaimedDebits_OnTheirStoredCategory()
    {
        // Documented superset (money-semantics §6): Top spendings sets aside debits a non-family
        // counterparty claimed, but the ledger's category filter still matches their stored category.
        var book = await SeedAsync((context, uah) =>
        {
            var broker = new Counterparty { UserId = UserId, Name = "Broker", FlowRole = FlowRoles.Investment };
            broker.Rules.Add(new CounterpartyRule { CounterpartyId = broker.Id, MatchType = "description_contains", Pattern = "Broker deposit" });
            var bank = new Counterparty { UserId = UserId, Name = "Mortgage", FlowRole = FlowRoles.Household };
            bank.Rules.Add(new CounterpartyRule { CounterpartyId = bank.Id, MatchType = "description_contains", Pattern = "Mortgage" });
            context.Counterparties.AddRange(broker, bank);
            context.Transactions.AddRange(
                Tx(uah, 4000m, "debit", "Broker deposit", category: null),
                Tx(uah, 9000m, "debit", "Mortgage October", "LOAN_PAYMENTS"));
        });
        var from = MonthWindow.StartOfMonthsAgo(2);
        var transactions = new TransactionRepository(book.Context);
        var accounts = new BankAccountRepository(book.Context);
        var classification = new CounterpartyClassificationService(
            new CounterpartyRepository(book.Context), transactions, accounts);
        var topCategories = await new MerchantCategoryStatisticsService(transactions, accounts, new TransferDetectionService())
            .GetTopCategoriesAsync(
                UserId, await classification.ClassifyForWindowAsync(UserId, 2), limit: 10, months: 2, from: from);
        var ledger = new GetAllTransactionsQueryHandler(transactions, accounts, classification);

        async Task<IEnumerable<string>> DrillDownAsync(string category) =>
            (await ledger.Handle(
                new GetAllTransactionsQuery(
                    UserId, new PagedRequest(0, 100), From: from, TransactionType: "debit", Categories: [category]),
                CancellationToken.None)).Transactions.Select(t => t.Description);

        topCategories.Select(c => c.Category).Should().NotContain("LOAN_PAYMENTS");
        (await DrillDownAsync("LOAN_PAYMENTS")).Should().Equal("Mortgage October");
        var uncategorized = topCategories.Single(c => c.Category == CategoryKeys.Uncategorized);
        (await DrillDownAsync(CategoryKeys.Uncategorized)).Should().BeEquivalentTo(
            ["Card payment without a category", "IBKR market data fee", "Broker deposit"],
            "the broker deposit is listed although the {0} USD Uncategorized total leaves it out", uncategorized.TotalSpend);
    }

    private static BankAccount Account(string bankName, string currency) =>
        new(UserId, $"ext-{Guid.NewGuid():N}", bankName, "checking", "1234", "Owner", currency, UserId, "test-provider");

    private static Transaction Tx(BankAccount account, decimal amount, string type, string description, string? category) =>
        new(account.Id, UserId, amount, DateTime.UtcNow.AddHours(-1), description, $"hash-{Guid.NewGuid():N}")
        {
            TransactionType = type,
            MerchantCategory = category,
        };
}
