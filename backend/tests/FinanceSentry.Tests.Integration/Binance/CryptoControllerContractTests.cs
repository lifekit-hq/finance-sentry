using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using FinanceSentry.Modules.CryptoSync.Domain.Repositories;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
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

namespace FinanceSentry.Tests.Integration.Binance;

// ── Contract tests: POST /api/v1/crypto/binance/connect ──────────────────────

public class CryptoControllerConnectContractTests(CryptoApiFactory factory) : IClassFixture<CryptoApiFactory>
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();
    private readonly CryptoApiFactory _factory = factory;

    [Fact]
    public async Task Connect_NoAuth_Returns401()
    {
        var anonClient = _factory.CreateClient();
        var response = await anonClient.PostAsJsonAsync("/api/v1/crypto/binance/connect",
            new { ApiKey = "key", ApiSecret = "secret" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Connect_MissingApiKey_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/crypto/binance/connect",
            new { ApiKey = "", ApiSecret = "secret" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorShape>();
        body!.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task Connect_MissingApiSecret_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/crypto/binance/connect",
            new { ApiKey = "key", ApiSecret = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Connect_BinanceRejectsCredentials_Returns422()
    {
        _factory.AdapterMock
            .Setup(a => a.ValidateCredentialsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BinanceException("Invalid API key."));

        _factory.CredentialRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BinanceCredential?)null);

        var response = await _client.PostAsJsonAsync("/api/v1/crypto/binance/connect",
            new { ApiKey = "badkey123", ApiSecret = "badsecret123" });
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<ErrorShape>();
        body!.ErrorCode.Should().Be("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Connect_ValidCredentials_Returns201WithShape()
    {
        _factory.SetupSuccessfulConnect();

        var response = await _client.PostAsJsonAsync("/api/v1/crypto/binance/connect",
            new { ApiKey = "validkey123", ApiSecret = "validsecret123" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<ConnectResponseShape>();
        body.Should().NotBeNull();
        body!.Message.Should().Contain("connected");
        body.HoldingsCount.Should().BeGreaterThanOrEqualTo(0);
        body.SyncedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Connect_AlreadyConnected_Returns409()
    {
        var existingCredential = BinanceCredential.Create(
            _factory.TestUserId,
            [1], [2], [3], [4], [5], [6], 1);

        _factory.CredentialRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingCredential);

        var response = await _client.PostAsJsonAsync("/api/v1/crypto/binance/connect",
            new { ApiKey = "key123", ApiSecret = "secret123" });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ErrorShape>();
        body!.ErrorCode.Should().Be("ALREADY_CONNECTED");
    }
}

// ── Contract tests: GET /api/v1/crypto/holdings ──────────────────────────────

public class CryptoControllerHoldingsContractTests(CryptoApiFactory factory) : IClassFixture<CryptoApiFactory>
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();
    private readonly CryptoApiFactory _factory = factory;

    [Fact]
    public async Task GetHoldings_NoAuth_Returns401()
    {
        var anonClient = _factory.CreateClient();
        var response = await anonClient.GetAsync("/api/v1/crypto/holdings");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetHoldings_NoAccountConnected_Returns200WithEmptyList()
    {
        _factory.HoldingRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await _client.GetAsync("/api/v1/crypto/holdings");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<HoldingsResponseShape>();
        body.Should().NotBeNull();
        body!.Provider.Should().Be("binance");
        body.Holdings.Should().BeEmpty();
        body.TotalUsdValue.Should().Be(0m);
    }

    [Fact]
    public async Task GetHoldings_WithHoldings_Returns200WithShape()
    {
        var holding = CryptoHolding.Create(
            _factory.TestUserId, "BTC", 0.5m, 0.1m, 30000m);

        _factory.HoldingRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([holding]);

        var response = await _client.GetAsync("/api/v1/crypto/holdings");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<HoldingsResponseShape>();
        body!.Holdings.Should().HaveCount(1);
        body.Holdings[0].Asset.Should().Be("BTC");
        body.TotalUsdValue.Should().Be(30000m);
    }
}

// ── Contract tests: DELETE /api/v1/crypto/binance/disconnect ─────────────────

public class CryptoControllerDisconnectContractTests(CryptoApiFactory factory) : IClassFixture<CryptoApiFactory>
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();
    private readonly CryptoApiFactory _factory = factory;

    [Fact]
    public async Task Disconnect_NoAuth_Returns401()
    {
        var anonClient = _factory.CreateClient();
        var response = await anonClient.DeleteAsync("/api/v1/crypto/binance/disconnect");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Disconnect_NoAccountConnected_Returns404()
    {
        _factory.CredentialRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BinanceCredential?)null);
        _factory.HoldingRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await _client.DeleteAsync("/api/v1/crypto/binance/disconnect");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<ErrorShape>();
        body!.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Disconnect_Connected_Returns204NoContent()
    {
        var credential = BinanceCredential.Create(
            _factory.TestUserId, [1], [2], [3], [4], [5], [6], 1);

        _factory.CredentialRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(credential);
        _factory.CredentialRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _factory.HoldingRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _factory.HoldingRepoMock
            .Setup(r => r.DeleteByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var response = await _client.DeleteAsync("/api/v1/crypto/binance/disconnect");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}

// ── Response shapes ───────────────────────────────────────────────────────────

public record ErrorShape(string Error, string ErrorCode);
public record ConnectResponseShape(string Message, int HoldingsCount, DateTime SyncedAt);
public record HoldingShape(string Asset, decimal FreeQuantity, decimal LockedQuantity, decimal UsdValue);
public record HoldingsResponseShape(
    string Provider,
    DateTime? SyncedAt,
    bool IsStale,
    List<HoldingShape> Holdings,
    decimal TotalUsdValue);

// ── Shared WebApplicationFactory for Crypto tests ─────────────────────────────

public class CryptoApiFactory : WebApplicationFactory<Program>
{
    public Mock<IBinanceCredentialRepository> CredentialRepoMock { get; } = new(MockBehavior.Loose);
    public Mock<ICryptoHoldingRepository> HoldingRepoMock { get; } = new(MockBehavior.Loose);
    public Mock<ICryptoExchangeAdapter> AdapterMock { get; } = new(MockBehavior.Loose);

    public Guid TestUserId { get; } = Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            ReplaceService(services, CredentialRepoMock.Object);
            ReplaceService(services, HoldingRepoMock.Object);
            ReplaceService<ICryptoExchangeAdapter>(services, AdapterMock.Object);

            ReplaceDbContextWithInMemory<FinanceSentry.Modules.BankSync.Infrastructure.Persistence.BankSyncDbContext>(
                services, $"CryptoTestBankSync_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.Auth.Infrastructure.Persistence.AuthDbContext>(
                services, $"CryptoTestAuth_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<CryptoSyncDbContext>(
                services, $"CryptoTestCrypto_{Guid.NewGuid()}");
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
    }

    public void SetupSuccessfulConnect()
    {
        // Capture the credential saved by ConnectBinanceCommandHandler so that
        // SyncBinanceHoldingsCommandHandler can retrieve it on the second call.
        BinanceCredential? capturedCredential = null;
        CredentialRepoMock
            .Setup(r => r.AddAsync(It.IsAny<BinanceCredential>(), It.IsAny<CancellationToken>()))
            .Callback<BinanceCredential, CancellationToken>((cred, _) => capturedCredential = cred)
            .Returns(Task.CompletedTask);
        CredentialRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => capturedCredential);
        CredentialRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        CredentialRepoMock
            .Setup(r => r.Update(It.IsAny<BinanceCredential>()));

        AdapterMock
            .Setup(a => a.ValidateCredentialsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        AdapterMock
            .Setup(a => a.GetHoldingsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        AdapterMock.Setup(a => a.ExchangeName).Returns("binance");

        HoldingRepoMock
            .Setup(r => r.UpsertRangeAsync(It.IsAny<IReadOnlyList<CryptoHolding>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        HoldingRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        HoldingRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
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
