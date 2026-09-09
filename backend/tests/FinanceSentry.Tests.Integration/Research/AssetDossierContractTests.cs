namespace FinanceSentry.Tests.Integration.Research;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Ports;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

// ── Contract tests: GET /api/v1/research/assets/{symbol}/dossier ─────────────
//
// Tests the shape contract of the aggregate dossier endpoint. All sub-query
// sources are stubbed to empty so the test is fast and self-contained.

public class AssetDossierContractTests(AssetDossierApiFactory factory)
    : IClassFixture<AssetDossierApiFactory>
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();
    private readonly AssetDossierApiFactory _factory = factory;

    [Fact]
    public async Task GetDossier_NoAuth_Returns401()
    {
        var anonClient = _factory.CreateClient();
        var response = await anonClient.GetAsync("/api/v1/research/assets/AAPL/dossier");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetDossier_NoHoldings_Returns200WithNullPosition()
    {
        // BookFiguresService returns empty — no position for any symbol.
        _factory.BookFiguresMock
            .Setup(s => s.ReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BookFigures.Empty);

        var response = await _client.GetAsync("/api/v1/research/assets/AAPL/dossier");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DossierShape>();
        body.Should().NotBeNull();
        body!.Symbol.Should().Be("AAPL");
        body.Position.Should().BeNull();
        body.Thesis.Should().BeNull();
        body.RecentNews.Should().NotBeNull();
        body.RadarSignals.Should().NotBeNull();
        body.GeneratedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task GetDossier_WithPosition_Returns200WithPositionSection()
    {
        // Simulate a single AAPL holding at $1500 with cost basis $1200.
        var pos = new BookFigurePosition("AAPL", "equity", 10m, 1200m, 1500m, "ibkr");
        var book = new BookFigures(0m, 0m, 0m, 1500m, 1500m, [pos], false, []);

        _factory.BookFiguresMock
            .Setup(s => s.ReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(book);

        // Tax lots port returns empty for this test.
        _factory.TaxLotsReaderMock
            .Setup(s => s.GetForSymbolAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await _client.GetAsync("/api/v1/research/assets/AAPL/dossier");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DossierShape>();
        body.Should().NotBeNull();
        body!.Symbol.Should().Be("AAPL");
        body.Position.Should().NotBeNull();
        body.Position!.Provider.Should().Be("ibkr");
        body.Position.Quantity.Should().Be(10m);
        body.Position.CurrentValueUsd.Should().Be(1500m);
        body.Position.CostBasisUsd.Should().Be(1200m);
        body.Position.UnrealizedPnlUsd.Should().Be(300m);
        body.Position.TaxLots.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDossier_SymbolUpperCased_Returns200()
    {
        _factory.BookFiguresMock
            .Setup(s => s.ReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BookFigures.Empty);

        // Lower-case input is normalised to upper-case in the response.
        var response = await _client.GetAsync("/api/v1/research/assets/aapl/dossier");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DossierShape>();
        body!.Symbol.Should().Be("AAPL");
    }
}

// ── Contract tests: {GET,POST} /api/v1/research/assets/{symbol}/ledger-read ──
//
// "Ledger's read" (feature 421, US3): generated on demand through the agent loop, cached
// server-side, invalidated daily or when the dossier facts move. The agent itself is stubbed
// via the ILedgerNarrator port so no model call is made.

public class AssetLedgerReadContractTests(AssetDossierApiFactory factory)
    : IClassFixture<AssetDossierApiFactory>
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();
    private readonly AssetDossierApiFactory _factory = factory;

    [Fact]
    public async Task GetLedgerRead_NoAuth_Returns401()
    {
        var anonClient = _factory.CreateClient();
        var response = await anonClient.GetAsync("/api/v1/research/assets/AAPL/ledger-read");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetLedgerRead_NothingGeneratedYet_Returns200WithNullNarrative()
    {
        var response = await _client.GetAsync("/api/v1/research/assets/NONE/ledger-read");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LedgerReadShape>();
        body!.Symbol.Should().Be("NONE");
        body.Narrative.Should().BeNull();
        body.Cached.Should().BeFalse();
        body.IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task GenerateLedgerRead_InvokesAgent_ThenServesCachedCopyInstantly()
    {
        _factory.NarratorMock
            .Setup(n => n.NarrateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("MSFT looks fine.");

        var generated = await _client.PostAsync("/api/v1/research/assets/MSFT/ledger-read", null);

        generated.StatusCode.Should().Be(HttpStatusCode.OK);
        var fresh = await generated.Content.ReadFromJsonAsync<LedgerReadShape>();
        fresh!.Narrative.Should().Be("MSFT looks fine.");
        fresh.Cached.Should().BeFalse("the first generation runs the agent");
        fresh.IsStale.Should().BeFalse();
        fresh.GeneratedAt.Should().NotBeNull();

        // The cached copy renders without regenerating.
        var cachedResponse = await _client.GetAsync("/api/v1/research/assets/MSFT/ledger-read");
        var cached = await cachedResponse.Content.ReadFromJsonAsync<LedgerReadShape>();
        cached!.Narrative.Should().Be("MSFT looks fine.");
        cached.Cached.Should().BeTrue();
        cached.IsStale.Should().BeFalse();

        _factory.NarratorMock.Verify(
            n => n.NarrateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once,
            "a GET must never invoke the agent");
    }

    [Fact]
    public async Task GenerateLedgerRead_FreshCache_SkipsTheAgent()
    {
        _factory.NarratorMock.Reset();
        _factory.NarratorMock
            .Setup(n => n.NarrateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("NVDA read.");

        await _client.PostAsync("/api/v1/research/assets/NVDA/ledger-read", null);
        var second = await _client.PostAsync("/api/v1/research/assets/NVDA/ledger-read", null);

        var body = await second.Content.ReadFromJsonAsync<LedgerReadShape>();
        body!.Narrative.Should().Be("NVDA read.");
        body.Cached.Should().BeTrue();

        _factory.NarratorMock.Verify(
            n => n.NarrateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateLedgerRead_Force_RegeneratesOverAFreshCache()
    {
        _factory.NarratorMock.Reset();
        _factory.NarratorMock
            .SetupSequence(n => n.NarrateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("first")
            .ReturnsAsync("second");

        await _client.PostAsync("/api/v1/research/assets/TSLA/ledger-read", null);
        var forced = await _client.PostAsync("/api/v1/research/assets/TSLA/ledger-read?force=true", null);

        var body = await forced.Content.ReadFromJsonAsync<LedgerReadShape>();
        body!.Narrative.Should().Be("second");
        body.Cached.Should().BeFalse();
    }

    [Fact]
    public async Task GetLedgerRead_AfterUnderlyingDataChanges_ReportsStale()
    {
        _factory.BookFiguresMock
            .Setup(s => s.ReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BookFigures.Empty);
        _factory.NarratorMock
            .Setup(n => n.NarrateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("AMD read.");

        await _client.PostAsync("/api/v1/research/assets/AMD/ledger-read", null);

        // The position appears after the read was generated — the dossier fingerprint moves.
        var pos = new BookFigurePosition("AMD", "equity", 5m, 500m, 700m, "ibkr");
        _factory.BookFiguresMock
            .Setup(s => s.ReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BookFigures(0m, 0m, 0m, 700m, 700m, [pos], false, []));
        _factory.TaxLotsReaderMock
            .Setup(s => s.GetForSymbolAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await _client.GetAsync("/api/v1/research/assets/AMD/ledger-read");

        var body = await response.Content.ReadFromJsonAsync<LedgerReadShape>();
        body!.Narrative.Should().Be("AMD read.", "a stale read is still shown, just flagged");
        body.Cached.Should().BeTrue();
        body.IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task GenerateLedgerRead_AgentUnavailable_Returns503()
    {
        _factory.NarratorMock
            .Setup(n => n.NarrateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var response = await _client.PostAsync("/api/v1/research/assets/INTC/ledger-read", null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadFromJsonAsync<LedgerReadErrorShape>();
        body!.ErrorCode.Should().Be("LEDGER_READ_UNAVAILABLE");
    }
}

// ── Response shapes for deserialization ──────────────────────────────────────

public record LedgerReadShape(
    string Symbol,
    string? Narrative,
    DateTimeOffset? GeneratedAt,
    bool IsStale,
    bool Cached);

public record LedgerReadErrorShape(string Error, string ErrorCode);


public record DossierPositionShape(
    string Provider,
    decimal Quantity,
    decimal CurrentValueUsd,
    decimal? CostBasisUsd,
    decimal? UnrealizedPnlUsd,
    decimal? UnrealizedPnlPercent,
    List<object> TaxLots);

public record DossierShape(
    string Symbol,
    DossierPositionShape? Position,
    object? Thesis,
    object? Valuation,
    object? Analysts,
    List<object> RecentNews,
    object? NextEarnings,
    List<object> RadarSignals,
    DateTimeOffset GeneratedAt);

// ── Shared WebApplicationFactory for dossier tests ───────────────────────────

public class AssetDossierApiFactory : WebApplicationFactory<Program>
{
    public Mock<IBookFiguresService> BookFiguresMock { get; } = new(MockBehavior.Loose);
    public Mock<IHoldingTaxLotsReader> TaxLotsReaderMock { get; } = new(MockBehavior.Loose);
    public Mock<IAssetSignalReader> SignalReaderMock { get; } = new(MockBehavior.Loose);
    public Mock<IValuationDataService> ValuationDataMock { get; } = new(MockBehavior.Loose);
    public Mock<IEarningsCalendarService> EarningsCalendarMock { get; } = new(MockBehavior.Loose);
    public Mock<IBrokerageHoldingsReader> BrokerageHoldingsReaderMock { get; } = new(MockBehavior.Loose);
    public Mock<IValuationHistoryService> ValuationHistoryMock { get; } = new(MockBehavior.Loose);
    public Mock<ILedgerNarrator> NarratorMock { get; } = new(MockBehavior.Loose);

    public Guid TestUserId { get; } = Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Stub out all cross-module ports and external-HTTP services.
            ReplaceService(services, BookFiguresMock.Object);
            ReplaceService(services, TaxLotsReaderMock.Object);
            ReplaceService(services, SignalReaderMock.Object);
            ReplaceService(services, ValuationDataMock.Object);
            ReplaceService(services, ValuationHistoryMock.Object);
            ReplaceService(services, EarningsCalendarMock.Object);
            ReplaceService(services, BrokerageHoldingsReaderMock.Object);
            ReplaceService(services, NarratorMock.Object);

            // Default stubs: return empty/null so tests that don't set up mocks still get 200.
            BookFiguresMock
                .Setup(s => s.ReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(BookFigures.Empty);
            SignalReaderMock
                .Setup(s => s.GetRecentAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            // ValuationDataMock returns null by default (MockBehavior.Loose): handler gracefully
            // returns ForNonEquity when GetCurrentMetricsAsync returns null — no explicit setup needed.
            EarningsCalendarMock
                .Setup(s => s.GetForTickersAsync(
                    It.IsAny<IReadOnlyCollection<string>>(),
                    It.IsAny<DateOnly>(),
                    It.IsAny<DateOnly>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            BrokerageHoldingsReaderMock
                .Setup(s => s.GetHoldingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            // Replace all DbContexts with in-memory databases.
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.BankSync.Infrastructure.Persistence.BankSyncDbContext>(
                services, $"DossierTestBankSync_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.Auth.Infrastructure.Persistence.AuthDbContext>(
                services, $"DossierTestAuth_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence.CryptoSyncDbContext>(
                services, $"DossierTestCrypto_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence.BrokerageSyncDbContext>(
                services, $"DossierTestBrokerage_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence.SubscriptionsDbContext>(
                services, $"DossierTestSubs_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.Alerts.Infrastructure.Persistence.AlertsDbContext>(
                services, $"DossierTestAlerts_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.Budgets.Infrastructure.Persistence.BudgetsDbContext>(
                services, $"DossierTestBudgets_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.Research.Infrastructure.Persistence.ResearchDbContext>(
                services, $"DossierTestResearch_{Guid.NewGuid()}");
        });

        builder.UseEnvironment("Testing");
        // Port 1 is deliberately unreachable. Every DbContext this factory does not replace above
        // still gets migrated by MigrateAllModules at startup; pointed at localhost:5432 those
        // attempts reach CI's postgres service container and fail authentication as user "test",
        // adding noise (and load) to the shared CI database for no benefit. Failing to connect
        // outright is both faster and inert — MigrateContext catches either way.
        builder.UseSetting("ConnectionStrings:Default",
            "Host=127.0.0.1;Port=1;Database=test;Username=test;Password=test;Timeout=1");
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
