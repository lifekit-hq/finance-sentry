namespace FinanceSentry.Tests.Integration.Companion;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Companion.API.Responses;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

public class ProblemReportsContractTests
{
    private const string Route = "/api/v1/feedback/problem-reports";

    private static object Body(string text = "The balance never refreshes.") => new
    {
        kind = "Broken",
        text,
        route = "/accounts/:id?tab=history",
        appVersion = "1.15.0",
        device = "Phone",
        os = "iOS",
        browser = "Safari",
    };

    [Fact]
    public async Task It_requires_sign_in()
    {
        using var factory = new ProblemReportApiFactory();

        (await factory.CreateClient().PostAsJsonAsync(Route, Body())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_linked_person_gets_202_with_a_reference_and_the_report_is_saved_cleaned()
    {
        using var factory = new ProblemReportApiFactory();

        var response = await factory.CreateAuthenticatedClient().PostAsJsonAsync(Route, Body("account 12345678 is wrong"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await response.Content.ReadFromJsonAsync<ProblemReportAcceptedDto>())!.Reference.Should().Be("FS-R-42");
        var saved = factory.Saved.Single();
        saved.UserId.Should().Be(factory.TestUserId);
        saved.Text.Should().Be("account [number removed] is wrong");
        saved.RoutePattern.Should().Be("/accounts/:id");
        saved.Status.Should().Be(ProblemReportStatus.Pending);
    }

    [Fact]
    public async Task A_person_without_a_logto_link_gets_403_and_nothing_is_saved()
    {
        using var factory = new ProblemReportApiFactory(linked: false);

        var response = await factory.CreateAuthenticatedClient().PostAsJsonAsync(Route, Body());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("REPORT_NOT_ALLOWED");
        factory.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Text_over_the_limit_is_a_validation_error()
    {
        using var factory = new ProblemReportApiFactory();

        var response = await factory.CreateAuthenticatedClient().PostAsJsonAsync(Route, Body(new string('a', 1001)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task The_sixth_report_in_an_hour_is_429_from_the_rate_limiter()
    {
        using var factory = new ProblemReportApiFactory();
        var client = factory.CreateAuthenticatedClient();

        for (var i = 0; i < ProblemReportLimits.PerHour; i++)
            (await client.PostAsJsonAsync(Route, Body())).StatusCode.Should().Be(HttpStatusCode.Accepted);

        (await client.PostAsJsonAsync(Route, Body())).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        factory.Saved.Should().HaveCount(ProblemReportLimits.PerHour);
    }

    [Fact]
    public async Task The_saved_rows_hold_the_cap_across_restarts_with_error_code_and_retry_after()
    {
        using var factory = new ProblemReportApiFactory();
        factory.Recent.AddRange(Enumerable.Range(0, ProblemReportLimits.PerHour).Select(i => DateTimeOffset.UtcNow.AddMinutes(-10 - i)));

        var response = await factory.CreateAuthenticatedClient().PostAsJsonAsync(Route, Body());

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await response.Content.ReadAsStringAsync()).Should().Contain("REPORT_RATE_LIMITED");
        response.Headers.RetryAfter!.Delta!.Value.TotalSeconds.Should().BeInRange(1, 3600);
        factory.Saved.Should().BeEmpty();
    }
}

public class ProblemReportApiFactory(bool linked = true) : WebApplicationFactory<Program>
{
    private const string Secret = "test-jwt-secret-key-for-integration-tests-minimum-32-chars";

    public Guid TestUserId { get; } = Guid.NewGuid();

    public List<ProblemReport> Saved { get; } = [];

    /// <summary>Creation times the (mocked) repository reports for the caller's recent reports.</summary>
    public List<DateTimeOffset> Recent { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var reports = new Mock<IProblemReportRepository>();
        reports.Setup(r => r.AddAsync(It.IsAny<ProblemReport>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProblemReport report, CancellationToken _) =>
            {
                report.Id = 42;
                Saved.Add(report);
                return report;
            });
        reports.Setup(r => r.ListCreatedAtSinceAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, DateTimeOffset since, CancellationToken _) =>
                (IReadOnlyList<DateTimeOffset>)Recent.Where(t => t >= since).Order().ToList());
        var links = new Mock<IOrgIdentityLinkReader>();
        links.Setup(l => l.IsLinkedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(linked);

        builder.ConfigureServices(services =>
        {
            Replace(services, reports.Object);
            Replace(services, links.Object);
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.Auth.Infrastructure.Persistence.AuthDbContext>(
                services, $"ProblemReportTestAuth_{Guid.NewGuid()}");
        });

        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", "Host=localhost;Database=test;Username=test;Password=test");
        builder.UseSetting("Deduplication:MasterKeyBase64", "dGVzdC1vbmx5LWtleS1ub3QtdGhlLWxlYWtlZC1vbmU=");
        builder.UseSetting("Encryption:CurrentKeyVersion", "1");
        builder.UseSetting("Encryption:Keys:1", "dGVzdC1vbmx5LWtleS1ub3QtdGhlLWxlYWtlZC1vbmU=");
        builder.UseSetting("Jwt:Secret", Secret);
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        TestUsers.EnsureExists(Services, TestUserId);
        client.DefaultRequestHeaders.Add("Cookie", $"fs_access_token={GenerateJwt(TestUserId)}");
        return client;
    }

    private static string GenerateJwt(Guid userId)
    {
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("sub", userId.ToString())]),
            Audience = FinanceSentry.Core.Auth.AuthAudiences.App,
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(System.Text.Encoding.ASCII.GetBytes(Secret)), SecurityAlgorithms.HmacSha256),
        }));
    }

    private static void Replace<T>(IServiceCollection services, T implementation) where T : class
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(T));
        if (descriptor != null) services.Remove(descriptor);
        services.AddScoped(_ => implementation);
    }

    private static void ReplaceDbContextWithInMemory<TContext>(IServiceCollection services, string dbName)
        where TContext : DbContext
    {
        foreach (var d in services
                     .Where(d => d.ServiceType == typeof(DbContextOptions<TContext>)
                              || d.ServiceType == typeof(TContext)
                              || d.ServiceType == typeof(IDbContextOptionsConfiguration<TContext>))
                     .ToList())
            services.Remove(d);

        services.AddDbContext<TContext>(options => options.UseInMemoryDatabase(dbName));
    }
}
