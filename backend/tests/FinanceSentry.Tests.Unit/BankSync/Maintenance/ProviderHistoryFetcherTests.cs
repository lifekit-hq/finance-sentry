namespace FinanceSentry.Tests.Unit.BankSync.Maintenance;

using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Application.Services.CategoryMapping;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Interfaces;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Maintenance;
using FinanceSentry.Modules.BankSync.Infrastructure.Monobank;
using FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;
using FinanceSentry.Tests.Unit.BankSync.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public class ProviderHistoryFetcherTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private const string AccessToken = "access-token";
    private const string ExternalId = "tl-ext-1";

    private readonly Mock<ITrueLayerClient> _client = new();
    private readonly Mock<ITrueLayerConnectionRepository> _connections = new();
    private readonly Mock<ITrueLayerTokenRefreshService> _tokenRefresh = new();
    private readonly TrueLayerConnection _connection = new(UserId, "ob-testbank", "Test Bank", "ref-1");

    public ProviderHistoryFetcherTests()
    {
        _connection.MarkLinked(null);
        _connections.Setup(r => r.GetByIdUnscopedAsync(_connection.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_connection);
        _tokenRefresh.Setup(s => s.AcquireAccessTokenAsync(_connection.Id, It.IsAny<CancellationToken>())).ReturnsAsync(AccessToken);
        _client.Setup(c => c.GetTransactionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync([Booked("account booking")]);
        _client.Setup(c => c.GetPendingTransactionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync([]);
        _client.Setup(c => c.GetCardTransactionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync([Booked("card booking")]);
        _client.Setup(c => c.GetCardPendingTransactionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync([]);
    }

    private static TrueLayerTransaction Booked(string description) =>
        new("tx-1", new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc), -3.50m, "EUR", description, null, "debit", false, []);

    private ProviderHistoryFetcher Sut()
    {
        var adapter = new TrueLayerAdapter(
            _client.Object, new TrueLayerCategoryMapper(), StubCategoryResolver.Categorizer, StubActiveSubscriptionsReader.Empty);
        var factory = new Mock<IBankProviderFactory>();
        factory.Setup(f => f.Resolve("truelayer")).Returns(adapter);
        return new ProviderHistoryFetcher(
            Mock.Of<ICredentialEncryptionService>(), factory.Object, Mock.Of<IMonobankCredentialRepository>(),
            Mock.Of<IMonobankAdapter>(), _connections.Object, _tokenRefresh.Object, NullLogger<ProviderHistoryFetcher>.Instance);
    }

    private BankAccount Account(string? productType) =>
        new(UserId, ExternalId, "Test Bank", productType == TrueLayerAdapter.CardProductType ? "credit" : "checking",
            "1234", "Owner", "EUR", UserId, "truelayer")
        {
            TrueLayerConnectionId = _connection.Id,
            ProductType = productType,
        };

    [Fact]
    public async Task CardAccount_IsReadFromTheCardEndpoints()
    {
        // TrueLayer serves credit cards under /data/v1/cards only; the accounts endpoints 404 for them.
        var candidates = await Sut().FetchAsync(Account(TrueLayerAdapter.CardProductType), DateTime.UtcNow.AddDays(-30));

        candidates.Should().ContainSingle().Which.Description.Should().Be("card booking");
        _client.Verify(c => c.GetCardTransactionsAsync(AccessToken, ExternalId, It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(c => c.GetTransactionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()), Times.Never);
        _client.Verify(c => c.GetPendingTransactionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegularAccount_IsReadFromTheAccountEndpoints()
    {
        var candidates = await Sut().FetchAsync(Account(null), DateTime.UtcNow.AddDays(-30));

        candidates.Should().ContainSingle().Which.Description.Should().Be("account booking");
        _client.Verify(c => c.GetCardTransactionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConnectionThatIsNotLinked_ReadsNothing()
    {
        _connection.MarkExpired();

        var candidates = await Sut().FetchAsync(Account(TrueLayerAdapter.CardProductType), DateTime.UtcNow.AddDays(-30));

        candidates.Should().BeEmpty();
        _tokenRefresh.Verify(s => s.AcquireAccessTokenAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
