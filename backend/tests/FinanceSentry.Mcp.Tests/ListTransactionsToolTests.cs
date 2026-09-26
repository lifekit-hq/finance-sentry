using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.BankSync.Application.Queries;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

public sealed class ListTransactionsToolTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IQueryHandler<GetAllTransactionsQuery, AllTransactionsResult>> _queryHandler = new();
    private readonly Mock<IBankingAccountsReader> _accountsReader = new();

    private ListTransactionsTool CreateSut() =>
        new(_queryHandler.Object, _accountsReader.Object, new FakeIdentityResolver(), NullLogger<ListTransactionsTool>.Instance);

    private static GlobalTransactionDto MakeTransaction(
        Guid? accountId = null,
        string? category = null,
        DateTime? date = null) =>
        new(
            Guid.NewGuid(),
            accountId ?? Guid.NewGuid(),
            "TestBank",
            "USD",
            100m,
            100m,
            date ?? DateTime.UtcNow,
            null,
            "Test description",
            "debit",
            category,
            null,
            false,
            DateTime.UtcNow);

    private static AllTransactionsResult EmptyResult() =>
        new([], 0, false, 0, 50);

    private static AllTransactionsResult ResultWith(params GlobalTransactionDto[] txns) =>
        new([.. txns], txns.Length, false, 0, txns.Length);

    [Fact]
    public async Task ExecuteAsync_ReturnsEmpty_WhenQueryHandlerThrows()
    {
        _queryHandler
            .Setup(h => h.Handle(It.IsAny<GetAllTransactionsQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        var result = await CreateSut().ExecuteAsync(UserId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsEmpty_WhenNoTransactionsExist()
    {
        _queryHandler
            .Setup(h => h.Handle(It.IsAny<GetAllTransactionsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyResult());
        _accountsReader
            .Setup(r => r.GetAccountSummariesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateSut().ExecuteAsync(UserId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_EnrichesWithCurrencyAndProvider_FromAccountMetadata()
    {
        var accountId = Guid.NewGuid();
        var txn = MakeTransaction(accountId);

        _queryHandler
            .Setup(h => h.Handle(It.IsAny<GetAllTransactionsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultWith(txn));
        _accountsReader
            .Setup(r => r.GetAccountSummariesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new BankingAccountSummary(accountId, "TestBank", "checking", "1234", "truelayer", "EUR", 1000m, null, "active", null)
            ]);

        var result = await CreateSut().ExecuteAsync(UserId);

        result.Should().HaveCount(1);
        var entry = result[0];
        entry.AccountId.Should().Be(accountId.ToString());
        entry.Amount.Should().Be(100m);
        entry.Currency.Should().Be("EUR");
        entry.Provider.Should().Be("truelayer");
        entry.Description.Should().Be("Test description");
    }

    [Fact]
    public async Task ExecuteAsync_FallsBackToUnknown_WhenAccountsReaderThrows()
    {
        var txn = MakeTransaction();

        _queryHandler
            .Setup(h => h.Handle(It.IsAny<GetAllTransactionsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultWith(txn));
        _accountsReader
            .Setup(r => r.GetAccountSummariesAsync(UserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("reader down"));

        var result = await CreateSut().ExecuteAsync(UserId);

        result.Should().HaveCount(1);
        result[0].Currency.Should().Be("unknown");
        result[0].Provider.Should().Be("unknown");
    }

    [Fact]
    public async Task ExecuteAsync_FiltersByAccountId()
    {
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();

        _queryHandler
            .Setup(h => h.Handle(It.IsAny<GetAllTransactionsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultWith(MakeTransaction(accountA), MakeTransaction(accountB)));
        _accountsReader
            .Setup(r => r.GetAccountSummariesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateSut().ExecuteAsync(UserId, accountId: accountA.ToString());

        result.Should().HaveCount(1);
        result[0].AccountId.Should().Be(accountA.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsEmpty_WhenAccountIdIsInvalidGuid()
    {
        // query handler should not even be called for a non-GUID accountId
        _accountsReader
            .Setup(r => r.GetAccountSummariesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateSut().ExecuteAsync(UserId, accountId: "not-a-guid");

        result.Should().BeEmpty();
        _queryHandler.Verify(
            h => h.Handle(It.IsAny<GetAllTransactionsQuery>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_FiltersByCategory_CaseInsensitive()
    {
        _queryHandler
            .Setup(h => h.Handle(It.IsAny<GetAllTransactionsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultWith(
                MakeTransaction(category: "FOOD_AND_DRINK"),
                MakeTransaction(category: "TRAVEL")));
        _accountsReader
            .Setup(r => r.GetAccountSummariesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateSut().ExecuteAsync(UserId, category: "food_and_drink");

        result.Should().HaveCount(1);
        result[0].Category.Should().Be("FOOD_AND_DRINK");
    }

    [Fact]
    public async Task ExecuteAsync_PaginatesResults_ByPageAndPageSize()
    {
        var txns = Enumerable.Range(0, 5).Select(_ => MakeTransaction()).ToArray();

        _queryHandler
            .Setup(h => h.Handle(It.IsAny<GetAllTransactionsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultWith(txns));
        _accountsReader
            .Setup(r => r.GetAccountSummariesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = CreateSut();
        var page1 = await sut.ExecuteAsync(UserId, page: 1, pageSize: 2);
        var page2 = await sut.ExecuteAsync(UserId, page: 2, pageSize: 2);
        var page3 = await sut.ExecuteAsync(UserId, page: 3, pageSize: 2);

        page1.Should().HaveCount(2);
        page2.Should().HaveCount(2);
        page3.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecuteAsync_PassesDateFiltersToQueryHandler()
    {
        var from = new DateOnly(2024, 1, 1);
        var to = new DateOnly(2024, 12, 31);

        GetAllTransactionsQuery? captured = null;
        _queryHandler
            .Setup(h => h.Handle(It.IsAny<GetAllTransactionsQuery>(), It.IsAny<CancellationToken>()))
            .Callback<GetAllTransactionsQuery, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(EmptyResult());
        _accountsReader
            .Setup(r => r.GetAccountSummariesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await CreateSut().ExecuteAsync(UserId, fromDate: from, toDate: to);

        captured.Should().NotBeNull();
        captured!.From.Should().Be(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        captured.To.Should().Be(to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc));
    }

    [Fact]
    public async Task ExecuteAsync_ClampsInvalidPage_ToFirstPage()
    {
        var txn = MakeTransaction();

        _queryHandler
            .Setup(h => h.Handle(It.IsAny<GetAllTransactionsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultWith(txn));
        _accountsReader
            .Setup(r => r.GetAccountSummariesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // page=0 should be treated as page=1
        var result = await CreateSut().ExecuteAsync(UserId, page: 0, pageSize: 10);

        result.Should().HaveCount(1);
    }
}
