using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Interfaces;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
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

namespace FinanceSentry.Tests.Integration.BrokerageSync;

// ── Contract tests: blocking IBKR connect ────────────────────────────────────
//
// POST /api/v1/brokerage/ibkr/connect awaits the full flow (spawn → auth →
// initial sync) and returns 200 with { holdingsCount, connectedAt, accountId }
// or a 4xx with { errorCode, errorMessage } on failure. No session polling.

public class BrokerageControllerConnectContractTests(BrokerageApiFactory factory) : IClassFixture<BrokerageApiFactory>
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();
    private readonly BrokerageApiFactory _factory = factory;

    private static object ValidBody() => new
    {
        ConsumerKey = "FINSENTRY",
        AccessToken = "access-token",
        AccessTokenSecret = "token-secret",
        SignatureKey = "sig-pem",
        EncryptionKey = "enc-pem",
        DhParam = "dh-pem",
    };

    [Fact]
    public async Task Connect_NoAuth_Returns401()
    {
        var anonClient = _factory.CreateClient();
        var response = await anonClient.PostAsJsonAsync(
            "/api/v1/brokerage/ibkr/connect", ValidBody());
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Connect_HappyPath_Returns200_AndPersistsArtifacts()
    {
        _factory.SetupSuccessfulConnect();

        var response = await _client.PostAsJsonAsync(
            "/api/v1/brokerage/ibkr/connect", ValidBody());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ResultShape>();
        result.Should().NotBeNull();
        // Live holdings sync runs out of band once the consumer key activates.
        result!.HoldingsCount.Should().Be(0);

        _factory.CredentialRepoMock.Verify(
            r => r.AddAsync(It.IsAny<IBKRCredential>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Connect_AlreadyConnected_Returns409_WithDUPLICATE()
    {
        _factory.SetupSuccessfulConnect();
        var existing = new IBKRCredential(
            _factory.TestUserId, "FINSENTRY", "access-token", "dh-pem",
            [1], [2], [3], [4], [5], [6], [7], [8], [9], keyVersion: 1);
        _factory.CredentialRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/brokerage/ibkr/connect", ValidBody());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<BrokerageErrorShape>();
        body!.ErrorCode.Should().Be("IBKR_DUPLICATE");
    }
}

// ── Response shapes ───────────────────────────────────────────────────────────

public record BrokerageErrorShape(string ErrorCode, string ErrorMessage);
public record ResultShape(int HoldingsCount, DateTime ConnectedAt, string AccountId);
public record BrokeragePositionShape(string Symbol, string InstrumentType, decimal Quantity, decimal UsdValue);
public record BrokerageHoldingsResponseShape(
    string Provider,
    DateTime? SyncedAt,
    bool IsStale,
    List<BrokeragePositionShape> Positions,
    decimal TotalUsdValue);

// ── Shared WebApplicationFactory ─────────────────────────────────────────────

public class BrokerageApiFactory : WebApplicationFactory<Program>
{
    public Mock<IIBKRCredentialRepository> CredentialRepoMock { get; } = new(MockBehavior.Loose);
    public Mock<IBrokerageHoldingRepository> HoldingRepoMock { get; } = new(MockBehavior.Loose);
    public Mock<IBrokerAdapter> AdapterMock { get; } = new(MockBehavior.Loose);

    public Guid TestUserId { get; } = Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            ReplaceService(services, CredentialRepoMock.Object);
            ReplaceService(services, HoldingRepoMock.Object);
            ReplaceService<IBrokerAdapter>(services, AdapterMock.Object);

            ReplaceDbContextWithInMemory<FinanceSentry.Modules.BankSync.Infrastructure.Persistence.BankSyncDbContext>(
                services, $"BrokerageTestBankSync_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.Auth.Infrastructure.Persistence.AuthDbContext>(
                services, $"BrokerageTestAuth_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence.CryptoSyncDbContext>(
                services, $"BrokerageTestCrypto_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<BrokerageSyncDbContext>(
                services, $"BrokerageTestBrokerage_{Guid.NewGuid()}");
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
        builder.UseSetting("Binance:BaseUrl", "https://testnet.binance.vision");
        builder.UseSetting("Binance:DustThresholdUsd", "0.01");
        builder.UseSetting("IBKR:GatewayBaseUrl", "http://localhost:9999");
    }

    public void SetupSuccessfulConnect()
    {
        // OAuth connect only persists artifacts — the real encryption service
        // (wired from the Encryption:Keys test config) encrypts the secrets.
        CredentialRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IBKRCredential?)null);
        CredentialRepoMock
            .Setup(r => r.AddAsync(It.IsAny<IBKRCredential>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        CredentialRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        CredentialRepoMock
            .Setup(r => r.Update(It.IsAny<IBKRCredential>()));
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
