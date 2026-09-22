namespace FinanceSentry.Tests.Integration.Events;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Events.Domain.Ports;
using FinanceSentry.Modules.Events.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

// ── Contract tests: GET /api/v1/events/upcoming and GET /api/v1/events/fired (feature 049) ──
//
// Every port the Events module reads through is stubbed, so the tests pin the HTTP contract
// (auth, status codes, shapes, query binding) and nothing external.

public class EventsContractTests(EventsApiFactory factory) : IClassFixture<EventsApiFactory>
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();
    private readonly EventsApiFactory _factory = factory;

    [Fact]
    public async Task GetUpcoming_NoAuth_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/events/upcoming");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetFired_NoAuth_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/events/fired");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetUpcoming_DefaultWindow_Returns200WithSourcesAndItems()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _factory.MacroMock
            .Setup(m => m.QueryAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MacroCalendarEntry(Guid.NewGuid(), today.AddDays(3), new TimeOnly(14, 0), "FOMC rate decision", "US", "high", "fed")]);

        var response = await _client.GetAsync("/api/v1/events/upcoming");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UpcomingShape>();
        body.Should().NotBeNull();
        body!.From.Should().Be(today);
        body.To.Should().Be(today.AddDays(90));
        body.Sources.Should().HaveCount(4).And.OnlyContain(s => s.Status == "ok");
        var item = body.Items.Should().ContainSingle().Subject;
        item.Kind.Should().Be("macro");
        item.Subject.Should().Be("US");
        item.Title.Should().Be("FOMC rate decision");
        item.Time.Should().Be(new TimeOnly(14, 0));
        item.IsEstimate.Should().BeFalse();
    }

    [Fact]
    public async Task GetUpcoming_KindsQuery_IsCommaSeparated()
    {
        var response = await _client.GetAsync("/api/v1/events/upcoming?kinds=macro,thesis_catalyst");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UpcomingShape>();
        body!.Sources.Select(s => s.Source).Should().BeEquivalentTo(["macro", "theses"]);
    }

    [Fact]
    public async Task GetUpcoming_InvalidWindow_Returns400WithErrorCode()
    {
        var response = await _client.GetAsync("/api/v1/events/upcoming?from=2026-09-22&to=2026-09-01");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorShape>();
        body!.ErrorCode.Should().Be("EVENTS_WINDOW_INVALID");
    }

    [Fact]
    public async Task GetUpcoming_UnavailableSource_StillReturns200()
    {
        _factory.MacroMock
            .Setup(m => m.QueryAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db gone"));

        var response = await _client.GetAsync("/api/v1/events/upcoming?kinds=macro");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UpcomingShape>();
        body!.Items.Should().BeEmpty();
        body.Sources.Should().ContainSingle(s => s.Source == "macro" && s.Status == "unavailable");
        _factory.MacroMock.Reset();
    }

    [Fact]
    public async Task GetFired_Returns200WithPagedShapeAndOutcome()
    {
        var alertId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        _factory.FiredAlertsMock
            .Setup(a => a.ListAsync(_factory.TestUserId, It.IsAny<IReadOnlyCollection<string>>(), 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FiredAlertPage([
                new FiredAlertRecord(alertId, "NewsCluster", "Warning", "News cluster: MU", "MU news clustered", null, "MU", false, false, DateTimeOffset.UtcNow),
            ], 1));
        _factory.DeliveryMock
            .Setup(d => d.ListForAlertsAsync(_factory.TestUserId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new EventDeliveryRecord(eventId, alertId, "NewsCluster", "Delivered", DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow)]);

        var response = await _client.GetAsync("/api/v1/events/fired");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<FiredPageShape>();
        body.Should().NotBeNull();
        body!.TotalCount.Should().Be(1);
        body.Page.Should().Be(1);
        body.PageSize.Should().Be(20);
        body.TotalPages.Should().Be(1);
        var item = body.Items.Should().ContainSingle().Subject;
        item.AlertId.Should().Be(alertId);
        item.Kind.Should().Be("NewsCluster");
        item.Subject.Should().Be("MU");
        item.Outcome.Should().Be("silent");
        item.Delivery!.EventId.Should().Be(eventId);
        item.Delivery.Disposition.Should().Be("Delivered");
        item.Verdict.Should().BeNull();
    }

    [Fact]
    public async Task GetFired_PagingIsClamped()
    {
        _factory.FiredAlertsMock
            .Setup(a => a.ListAsync(_factory.TestUserId, It.IsAny<IReadOnlyCollection<string>>(), 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FiredAlertPage([], 0));

        var response = await _client.GetAsync("/api/v1/events/fired?page=0&pageSize=999");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<FiredPageShape>();
        body!.Page.Should().Be(1);
        body.PageSize.Should().Be(20);
    }

    // ── Response shapes (deserialization targets) ──

    private sealed record SourceShape(string Source, string Status);
    private sealed record UpcomingItemShape(string Kind, DateOnly Date, TimeOnly? Time, string Subject, string Title, string? Detail, bool IsEstimate, string Source, Guid? ReferenceId);
    private sealed record UpcomingShape(List<UpcomingItemShape> Items, DateOnly From, DateOnly To, List<SourceShape> Sources);
    private sealed record DeliveryShape(Guid EventId, string Disposition, DateTimeOffset? DispatchedAt, DateTimeOffset? DeliveredAt);
    private sealed record VerdictShape(string Text, bool Notified, DateTimeOffset RecordedAt);
    private sealed record FiredItemShape(Guid AlertId, string Kind, string Severity, string Subject, string Title, string Message, DateTimeOffset OccurredAt, bool IsRead, DeliveryShape? Delivery, VerdictShape? Verdict, string Outcome);
    private sealed record FiredPageShape(List<FiredItemShape> Items, int TotalCount, int Page, int PageSize, int TotalPages);
    private sealed record ErrorShape(string Error, string ErrorCode);
}

// ── Shared WebApplicationFactory for the events endpoints ──

public class EventsApiFactory : WebApplicationFactory<Program>
{
    public Mock<IBrokerageHoldingsReader> BrokerageMock { get; } = new(MockBehavior.Loose);
    public Mock<IWatchlistReader> WatchlistMock { get; } = new(MockBehavior.Loose);
    public Mock<IUpcomingCorporateEventReader> CorporateMock { get; } = new(MockBehavior.Loose);
    public Mock<IMacroEventReader> MacroMock { get; } = new(MockBehavior.Loose);
    public Mock<IThesisCatalystReader> ThesesMock { get; } = new(MockBehavior.Loose);
    public Mock<IPeriodicFilingReader> FilingsMock { get; } = new(MockBehavior.Loose);
    public Mock<IFiredAlertReader> FiredAlertsMock { get; } = new(MockBehavior.Loose);
    public Mock<IEventDeliveryReader> DeliveryMock { get; } = new(MockBehavior.Loose);

    public Guid TestUserId { get; } = Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            ReplaceService(services, BrokerageMock.Object);
            ReplaceService(services, WatchlistMock.Object);
            ReplaceService(services, CorporateMock.Object);
            ReplaceService(services, MacroMock.Object);
            ReplaceService(services, ThesesMock.Object);
            ReplaceService(services, FilingsMock.Object);
            ReplaceService(services, FiredAlertsMock.Object);
            ReplaceService(services, DeliveryMock.Object);

            // Every port answers empty unless a test says otherwise, so an unconfigured source reads
            // as "ok, nothing scheduled" rather than throwing.
            BrokerageMock
                .Setup(b => b.GetHoldingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            WatchlistMock
                .Setup(w => w.ListTickersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            FiredAlertsMock
                .Setup(a => a.ListAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FiredAlertPage([], 0));
            ThesesMock
                .Setup(t => t.ListActiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            CorporateMock
                .Setup(c => c.GetForTickersAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            FilingsMock
                .Setup(f => f.GetRecentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            DeliveryMock
                .Setup(d => d.ListForAlertsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            ReplaceDbContextWithInMemory<EventsDbContext>(services, $"EventsTest_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.Auth.Infrastructure.Persistence.AuthDbContext>(
                services, $"EventsTestAuth_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.BankSync.Infrastructure.Persistence.BankSyncDbContext>(
                services, $"EventsTestBankSync_{Guid.NewGuid()}");
        });

        builder.UseEnvironment("Testing");
        // Port 1 is deliberately unreachable: contexts this factory does not replace fail their
        // startup migration fast and inertly (MigrateContext catches), see AssetDossierApiFactory.
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
