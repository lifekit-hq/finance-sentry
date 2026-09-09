namespace FinanceSentry.Tests.Integration.Subscriptions;

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FinanceSentry.Modules.Subscriptions.Domain;
using FinanceSentry.Modules.Subscriptions.Domain.Repositories;
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

public class SubscriptionsContractTests(SubscriptionsApiFactory factory)
    : IClassFixture<SubscriptionsApiFactory>
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();
    private readonly SubscriptionsApiFactory _factory = factory;

    [Fact]
    public async Task GetSubscriptions_NoAuth_Returns401()
    {
        var anon = _factory.CreateClient();
        var response = await anon.GetAsync("/api/v1/subscriptions");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSubscriptions_Empty_ReturnsCorrectShape()
    {
        _factory.RepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DetectedSubscription>().ToList());
        _factory.RepoMock
            .Setup(r => r.GetActiveByUserIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DetectedSubscription>().ToList());

        var response = await _client.GetAsync("/api/v1/subscriptions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SubscriptionsListShape>();
        body.Should().NotBeNull();
        body!.Items.Should().BeEmpty();
        body.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetSummary_NoAuth_Returns401()
    {
        var anon = _factory.CreateClient();
        var response = await anon.GetAsync("/api/v1/subscriptions/summary");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSummary_Returns200WithShape()
    {
        _factory.RepoMock
            .Setup(r => r.GetActiveByUserIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DetectedSubscription>().ToList());
        _factory.RepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DetectedSubscription>().ToList());

        var response = await _client.GetAsync("/api/v1/subscriptions/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SummaryShape>();
        body.Should().NotBeNull();
        body!.TotalMonthlyEstimate.Should().Be(0);
        body.ActiveCount.Should().Be(0);
    }

    [Fact]
    public async Task Dismiss_NoAuth_Returns401()
    {
        var anon = _factory.CreateClient();
        var response = await anon.PatchAsync($"/api/v1/subscriptions/{Guid.NewGuid()}/dismiss", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dismiss_UnknownId_Returns404()
    {
        _factory.RepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DetectedSubscription?)null);

        var response = await _client.PatchAsync($"/api/v1/subscriptions/{Guid.NewGuid()}/dismiss", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Restore_NoAuth_Returns401()
    {
        var anon = _factory.CreateClient();
        var response = await anon.PatchAsync($"/api/v1/subscriptions/{Guid.NewGuid()}/restore", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Restore_UnknownId_Returns404()
    {
        _factory.RepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DetectedSubscription?)null);

        var response = await _client.PatchAsync($"/api/v1/subscriptions/{Guid.NewGuid()}/restore", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private record SubscriptionsListShape(
        IReadOnlyList<object> Items, int TotalCount, bool HasInsufficientHistory);

    private record SummaryShape(
        decimal TotalMonthlyEstimate, decimal TotalAnnualEstimate,
        int ActiveCount, int PotentiallyCancelledCount, string Currency);
}

public class SubscriptionsApiFactory : WebApplicationFactory<Program>
{
    public Mock<IDetectedSubscriptionRepository> RepoMock { get; } = new(MockBehavior.Loose);
    public Guid TestUserId { get; } = Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            ReplaceService(services, RepoMock.Object);

            ReplaceDbContextWithInMemory<FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence.SubscriptionsDbContext>(
                services, $"SubsTest_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.BankSync.Infrastructure.Persistence.BankSyncDbContext>(
                services, $"SubsBankSync_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.Auth.Infrastructure.Persistence.AuthDbContext>(
                services, $"SubsAuth_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.Alerts.Infrastructure.Persistence.AlertsDbContext>(
                services, $"SubsAlerts_{Guid.NewGuid()}");

            services.Configure<FinanceSentry.Infrastructure.Encryption.EncryptionOptions>(opts =>
            {
                opts.CurrentKeyVersion = 1;
                opts.Keys = new Dictionary<int, string>
                {
                    [1] = "dGVzdC1vbmx5LWtleS1ub3QtdGhlLWxlYWtlZC1vbmU=",
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
