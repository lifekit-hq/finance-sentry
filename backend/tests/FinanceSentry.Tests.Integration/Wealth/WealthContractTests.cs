namespace FinanceSentry.Tests.Integration.Wealth;

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using Xunit;

// ── Contract tests: GET /api/v1/wealth/summary ───────────────────────────────

public class WealthSummaryContractTests(WealthApiFactory factory) : IClassFixture<WealthApiFactory>
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();
    private readonly WealthApiFactory _factory = factory;

    [Fact]
    public async Task GetSummary_NoAuth_Returns401()
    {
        var anonClient = _factory.CreateClient();
        var response = await anonClient.GetAsync("/api/v1/wealth/summary");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSummary_EmptyAccounts_Returns200WithEmptyCategories()
    {
        _factory.BankAccountRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await _client.GetAsync("/api/v1/wealth/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<WealthSummaryShape>();
        body.Should().NotBeNull();
        body!.TotalNetWorth.Should().Be(0);
        body.BaseCurrency.Should().Be("USD");
        body.Categories.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSummary_WithAccounts_Returns200WithFullShape()
    {
        var userId = _factory.TestUserId;
        var acc = new BankAccount(userId, "ext_001", "Chase", "checking", "1234", "Owner", "USD", userId, "truelayer");
        acc.BeginSync();
        acc.MarkActive(1000m);

        _factory.BankAccountRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([acc]);

        var response = await _client.GetAsync("/api/v1/wealth/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<WealthSummaryShape>();
        body!.TotalNetWorth.Should().Be(1000m);
        body.Categories.Should().HaveCount(1);
        body.Categories[0].Category.Should().Be("banking");
        body.Categories[0].Institutions.Should().HaveCount(1);
        body.Categories[0].Institutions[0].Accounts.Should().HaveCount(1);
        body.AppliedFilters.Category.Should().BeNull();
        body.AppliedFilters.Provider.Should().BeNull();
    }

    [Fact]
    public async Task GetSummary_InvalidCategory_Returns400()
    {
        var response = await _client.GetAsync("/api/v1/wealth/summary?category=bogus");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorShape>();
        body!.ErrorCode.Should().Be("INVALID_FILTER");
    }

    [Fact]
    public async Task GetSummary_CategoryFilter_Returns200()
    {
        _factory.BankAccountRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await _client.GetAsync("/api/v1/wealth/summary?category=banking");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<WealthSummaryShape>();
        body!.AppliedFilters.Category.Should().Be("banking");
    }
}

// ── Contract tests: GET /api/v1/wealth/transactions/summary ──────────────────

public class TransactionSummaryContractTests(WealthApiFactory factory) : IClassFixture<WealthApiFactory>
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();
    private readonly WealthApiFactory _factory = factory;

    [Fact]
    public async Task GetTxSummary_NoAuth_Returns401()
    {
        var anonClient = _factory.CreateClient();
        var response = await anonClient.GetAsync("/api/v1/wealth/transactions/summary?from=2026-04-01&to=2026-04-30");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetTxSummary_MissingFrom_Returns400MissingDateRange()
    {
        var response = await _client.GetAsync("/api/v1/wealth/transactions/summary?to=2026-04-30");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorShape>();
        body!.ErrorCode.Should().Be("MISSING_DATE_RANGE");
    }

    [Fact]
    public async Task GetTxSummary_MissingTo_Returns400MissingDateRange()
    {
        var response = await _client.GetAsync("/api/v1/wealth/transactions/summary?from=2026-04-01");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorShape>();
        body!.ErrorCode.Should().Be("MISSING_DATE_RANGE");
    }

    [Fact]
    public async Task GetTxSummary_FromAfterTo_Returns400InvalidDateRange()
    {
        var response = await _client.GetAsync("/api/v1/wealth/transactions/summary?from=2026-04-30&to=2026-04-01");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorShape>();
        body!.ErrorCode.Should().Be("INVALID_DATE_RANGE");
    }

    [Fact]
    public async Task GetTxSummary_EmptyWindow_Returns200WithZeros()
    {
        _factory.BankAccountRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _factory.TransactionRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await _client.GetAsync("/api/v1/wealth/transactions/summary?from=2026-04-01&to=2026-04-30");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TxSummaryShape>();
        body!.TotalDebits.Should().Be(0);
        body.TotalCredits.Should().Be(0);
        body.NetFlow.Should().Be(0);
        body.Categories.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTxSummary_ValidRequest_Returns200WithCorrectShape()
    {
        _factory.BankAccountRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _factory.TransactionRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await _client.GetAsync("/api/v1/wealth/transactions/summary?from=2026-04-01&to=2026-04-30");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TxSummaryShape>();
        body!.From.Should().Be("2026-04-01");
        body.To.Should().Be("2026-04-30");
        body.AppliedFilters.Should().NotBeNull();
    }
}

// ── Response shapes for deserialization ──────────────────────────────────────

public record AppliedFiltersShape(string? Category, string? Provider);
public record AccountShape(Guid Id, string BankName, string Provider, string Category,
    decimal? CurrentBalance, decimal? BalanceInBaseCurrency, string SyncStatus);
public record InstitutionShape(Guid InstitutionId, string Provider, string Name,
    decimal TotalInBaseCurrency, string SyncStatus, List<AccountShape> Accounts);
public record CategoryShape(string Category, decimal TotalInBaseCurrency, List<InstitutionShape> Institutions);
public record WealthSummaryShape(decimal TotalNetWorth, string BaseCurrency,
    List<CategoryShape> Categories, AppliedFiltersShape AppliedFilters);

public record TxCategoryShape(string Category, decimal TotalDebits, decimal TotalCredits,
    decimal NetFlow, int TransactionCount);
public record TxSummaryShape(string From, string To, decimal TotalDebits, decimal TotalCredits,
    decimal NetFlow, List<TxCategoryShape> Categories, AppliedFiltersShape AppliedFilters);

public record ErrorShape(string Error, string ErrorCode);

// ── Shared WebApplicationFactory for Wealth tests ─────────────────────────────

public class WealthApiFactory : WebApplicationFactory<Program>
{
    public Mock<IBankAccountRepository> BankAccountRepoMock { get; } = new(MockBehavior.Loose);
    public Mock<ITransactionRepository> TransactionRepoMock { get; } = new(MockBehavior.Loose);

    public Guid TestUserId { get; } = Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            ReplaceService(services, BankAccountRepoMock.Object);
            ReplaceService(services, TransactionRepoMock.Object);

            // Remove real DbContexts and replace with in-memory
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.BankSync.Infrastructure.Persistence.BankSyncDbContext>(
                services, $"WealthTestBankSync_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.Auth.Infrastructure.Persistence.AuthDbContext>(
                services, $"WealthTestAuth_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence.CryptoSyncDbContext>(
                services, $"WealthTestCrypto_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence.BrokerageSyncDbContext>(
                services, $"WealthTestBrokerage_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence.SubscriptionsDbContext>(
                services, $"WealthTestSubs_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.Alerts.Infrastructure.Persistence.AlertsDbContext>(
                services, $"WealthTestAlerts_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.Budgets.Infrastructure.Persistence.BudgetsDbContext>(
                services, $"WealthTestBudgets_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.Wealth.Infrastructure.Persistence.WealthDbContext>(
                services, $"WealthTestWealth_{Guid.NewGuid()}");

            services.Configure<FinanceSentry.Infrastructure.Encryption.EncryptionOptions>(opts =>
            {
                opts.CurrentKeyVersion = 1;
                opts.Keys = new Dictionary<int, string>
                {
                    [1] = "dGVzdC1vbmx5LWtleS1ub3QtdGhlLWxlYWtlZC1vbmU="
                };
            });
        });

        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default",
            "Host=localhost;Database=test;Username=test;Password=test");
        builder.UseSetting("Deduplication:MasterKeyBase64",
            "dGVzdC1vbmx5LWtleS1ub3QtdGhlLWxlYWtlZC1vbmU=");
        builder.UseSetting("Encryption:CurrentKeyVersion", "1");
        builder.UseSetting("Encryption:Keys:1",
            "dGVzdC1vbmx5LWtleS1ub3QtdGhlLWxlYWtlZC1vbmU=");
        builder.UseSetting("Jwt:Secret",
            "test-jwt-secret-key-for-integration-tests-minimum-32-chars");
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Cookie", $"fs_access_token={GenerateTestJwt(TestUserId)}");
        return client;
    }

    private static string GenerateTestJwt(Guid userId)
    {
        const string secret = "test-jwt-secret-key-for-integration-tests-minimum-32-chars";
        var key = new SymmetricSecurityKey(System.Text.Encoding.ASCII.GetBytes(secret));
        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("sub", userId.ToString())]),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        });
        return handler.WriteToken(token);
    }

    private static void ReplaceService<T>(IServiceCollection services, T implementation)
        where T : class
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(T));
        if (descriptor != null)
            services.Remove(descriptor);
        services.AddScoped(_ => implementation);
    }

    private static void ReplaceDbContextWithInMemory<TContext>(IServiceCollection services, string dbName)
        where TContext : DbContext
    {
        var toRemove = services
            .Where(d => d.ServiceType == typeof(DbContextOptions<TContext>)
                     || d.ServiceType == typeof(TContext)
                     || d.ServiceType == typeof(IDbContextOptionsConfiguration<TContext>))
            .ToList();
        foreach (var d in toRemove)
            services.Remove(d);

        services.AddDbContext<TContext>(options =>
            options.UseInMemoryDatabase(dbName));
    }
}
