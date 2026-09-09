namespace FinanceSentry.Tests.Integration.BankSync;

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
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

/// <summary>
/// REST API contract tests (T215).
/// Validates response shapes for the US1 endpoints:
///   GET  /accounts
///   GET  /accounts/{id}/transactions
///
/// Uses WebApplicationFactory with mocked infrastructure dependencies
/// (no real provider calls, no real database).
/// </summary>
public class BankSyncAPIContractTests(BankSyncApiFactory factory) : IClassFixture<BankSyncApiFactory>
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();
    private readonly BankSyncApiFactory _factory = factory;

    // ── GET /accounts ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAccounts_Returns200_WithAccountListShape()
    {
        _factory.BankAccountRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await _client.GetAsync("/api/v1/accounts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AccountsListResponse>();
        body.Should().NotBeNull();
        body!.Accounts.Should().NotBeNull();
        body.TotalCount.Should().Be(0);
        body.CurrencyTotals.Should().NotBeNull();
    }

    // ── GET /accounts/{id}/transactions ─────────────────────────────────────

    [Fact]
    public async Task GetTransactions_Returns404_WhenAccountNotOwnedByUser()
    {
        var accountId = Guid.NewGuid();
        var requestingUserId = Guid.NewGuid();

        // Repository returns account owned by a DIFFERENT user
        _factory.BankAccountRepoMock
            .Setup(r => r.GetByIdAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Modules.BankSync.Domain.BankAccount(
                userId: Guid.NewGuid(), // different user
                externalAccountId: "item_xxx",
                bankName: "AIB",
                accountType: "checking",
                accountNumberLast4: "1234",
                ownerName: "Other Person",
                currency: "EUR",
                createdBy: Guid.NewGuid(),
                provider: "truelayer"));

        var url = $"/api/v1/accounts/{accountId}/transactions?userId={requestingUserId}";
        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "FR-009: users must not access other users' account data");
    }

    [Fact]
    public async Task GetTransactions_Returns200_WithPaginatedShape()
    {
        var accountId = Guid.NewGuid();
        var userId = _factory.TestUserId; // must match the JWT sub claim

        _factory.BankAccountRepoMock
            .Setup(r => r.GetByIdAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Modules.BankSync.Domain.BankAccount(
                userId: userId, // same user as JWT sub
                externalAccountId: "item_abc",
                bankName: "Revolut",
                accountType: "checking",
                accountNumberLast4: "5678",
                ownerName: "Jane Doe",
                currency: "EUR",
                createdBy: userId,
                provider: "truelayer"));

        _factory.TransactionRepoMock
            .Setup(r => r.GetByAccountIdAsync(accountId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Modules.BankSync.Domain.Transaction>());
        _factory.TransactionRepoMock
            .Setup(r => r.CountByAccountIdAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var url = $"/api/v1/accounts/{accountId}/transactions?offset=0&limit=50";
        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TransactionsListResponse>();
        body.Should().NotBeNull();
        body!.Items.Should().NotBeNull();
        body.TotalCount.Should().Be(0);
        body.HasMore.Should().BeFalse();
    }

    // ── Response shape records (contract definitions) ────────────────────────

    private record AccountsListResponse(object[] Accounts, int TotalCount, object CurrencyTotals);
    private record TransactionsListResponse(string AccountId, string BankName, string Currency,
        object[] Items, int TotalCount, int Offset, int Limit, bool HasMore);
}

/// <summary>
/// Custom WebApplicationFactory that replaces infrastructure with mocks.
/// </summary>
public class BankSyncApiFactory : WebApplicationFactory<Program>
{
    public Mock<IBankAccountRepository> BankAccountRepoMock { get; } = new(MockBehavior.Loose);
    public Mock<ITransactionRepository> TransactionRepoMock { get; } = new(MockBehavior.Loose);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Override real infrastructure with mocks
            ReplaceService(services, BankAccountRepoMock.Object);
            ReplaceService(services, TransactionRepoMock.Object);

            // Replace both Npgsql DbContexts with in-memory equivalents to avoid
            // real Postgres connections and the EF9 multiple-provider conflict.
            ReplaceDbContextWithInMemory<BankSyncDbContext>(services, "banksync-contract-tests");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.Auth.Infrastructure.Persistence.AuthDbContext>(services, "auth-contract-tests-bs");

            // Provide minimal configuration to satisfy Program.cs startup
            services.Configure<Infrastructure.Encryption.EncryptionOptions>(opts =>
            {
                opts.CurrentKeyVersion = 1;
                opts.Keys = new Dictionary<int, string>
                {
                    [1] = "dGVzdC1vbmx5LWtleS1ub3QtdGhlLWxlYWtlZC1vbmU=" // test key
                };
            });
        });

        builder.UseEnvironment("Testing");

        // Inject required config values so Program.cs startup checks don't throw
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

    /// <summary>The userId embedded in the test JWT — use this when seeding mock account data.</summary>
    public Guid TestUserId { get; } = Guid.NewGuid();

    public HttpClient CreateAuthenticatedClient()
    {
        // AllowAutoRedirect = false prevents HttpsRedirection middleware from
        // causing a scheme-change redirect that strips the Authorization header.
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
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
        // EF9: remove all provider-specific registrations to avoid multiple-provider conflict
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
