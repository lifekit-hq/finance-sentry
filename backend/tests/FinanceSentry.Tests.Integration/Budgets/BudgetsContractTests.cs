namespace FinanceSentry.Tests.Integration.Budgets;

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using FinanceSentry.Modules.Budgets.Domain.Repositories;
using FinanceSentry.Modules.Budgets.Domain;
using Xunit;

public class BudgetsContractTests(BudgetsApiFactory factory) : IClassFixture<BudgetsApiFactory>
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();
    private readonly BudgetsApiFactory _factory = factory;

    // GET /api/v1/budgets

    [Fact]
    public async Task GetBudgets_NoAuth_Returns401()
    {
        var anon = _factory.CreateClient();
        var response = await anon.GetAsync("/api/v1/budgets");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetBudgets_Empty_Returns200WithShape()
    {
        _factory.BudgetRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Budget>());

        var response = await _client.GetAsync("/api/v1/budgets");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BudgetsListShape>();
        body.Should().NotBeNull();
        body!.Items.Should().BeEmpty();
        body.TotalCount.Should().Be(0);
    }

    // POST /api/v1/budgets

    [Fact]
    public async Task CreateBudget_NoAuth_Returns401()
    {
        var anon = _factory.CreateClient();
        var response = await anon.PostAsJsonAsync("/api/v1/budgets",
            new { category = "FOOD_AND_DRINK", monthlyLimit = 400m });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateBudget_InvalidCategory_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/budgets",
            new { category = "invalid_cat", monthlyLimit = 400m });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorShape>();
        body!.ErrorCode.Should().Be("BUDGET_INVALID_CATEGORY");
    }

    [Fact]
    public async Task CreateBudget_ZeroLimit_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/budgets",
            new { category = "FOOD_AND_DRINK", monthlyLimit = 0m });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorShape>();
        body!.ErrorCode.Should().Be("BUDGET_INVALID_LIMIT");
    }

    [Fact]
    public async Task CreateBudget_DuplicateCategory_Returns409()
    {
        _factory.BudgetRepoMock
            .Setup(r => r.FindByUserAndCategoryAsync(It.IsAny<Guid>(), "FOOD_AND_DRINK", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Budget.Create(Guid.NewGuid(), "FOOD_AND_DRINK", 400m, "USD"));

        var response = await _client.PostAsJsonAsync("/api/v1/budgets",
            new { category = "FOOD_AND_DRINK", monthlyLimit = 400m });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ErrorShape>();
        body!.ErrorCode.Should().Be("BUDGET_DUPLICATE_CATEGORY");
    }

    [Fact]
    public async Task CreateBudget_Valid_Returns201WithDto()
    {
        var budget = Budget.Create(_factory.TestUserId, "FOOD_AND_DRINK", 400m, "USD");
        _factory.BudgetRepoMock
            .Setup(r => r.FindByUserAndCategoryAsync(It.IsAny<Guid>(), "FOOD_AND_DRINK", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Budget?)null);
        _factory.BudgetRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<Budget>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Budget b, CancellationToken _) => b);

        var response = await _client.PostAsJsonAsync("/api/v1/budgets",
            new { category = "FOOD_AND_DRINK", monthlyLimit = 400m });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<BudgetDtoShape>();
        body.Should().NotBeNull();
        body!.Category.Should().Be("FOOD_AND_DRINK");
        body.CategoryLabel.Should().Be("Food & Drink");
        body.MonthlyLimit.Should().Be(400m);
    }

    // PUT /api/v1/budgets/{id}

    [Fact]
    public async Task UpdateBudget_NoAuth_Returns401()
    {
        var anon = _factory.CreateClient();
        var response = await anon.PutAsJsonAsync($"/api/v1/budgets/{Guid.NewGuid()}",
            new { monthlyLimit = 500m });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateBudget_ZeroLimit_Returns400()
    {
        var response = await _client.PutAsJsonAsync($"/api/v1/budgets/{Guid.NewGuid()}",
            new { monthlyLimit = 0m });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorShape>();
        body!.ErrorCode.Should().Be("BUDGET_INVALID_LIMIT");
    }

    [Fact]
    public async Task UpdateBudget_NotFound_Returns404()
    {
        _factory.BudgetRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Budget?)null);

        var response = await _client.PutAsJsonAsync($"/api/v1/budgets/{Guid.NewGuid()}",
            new { monthlyLimit = 500m });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateBudget_Valid_Returns200WithUpdatedDto()
    {
        var budget = Budget.Create(_factory.TestUserId, "FOOD_AND_DRINK", 400m, "USD");
        _factory.BudgetRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(budget);
        _factory.BudgetRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<Budget>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var response = await _client.PutAsJsonAsync($"/api/v1/budgets/{budget.Id}",
            new { monthlyLimit = 500m });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BudgetDtoShape>();
        body!.MonthlyLimit.Should().Be(500m);
    }

    // DELETE /api/v1/budgets/{id}

    [Fact]
    public async Task DeleteBudget_NoAuth_Returns401()
    {
        var anon = _factory.CreateClient();
        var response = await anon.DeleteAsync($"/api/v1/budgets/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteBudget_NotFound_Returns404()
    {
        _factory.BudgetRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Budget?)null);

        var response = await _client.DeleteAsync($"/api/v1/budgets/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteBudget_Existing_Returns204()
    {
        var budget = Budget.Create(_factory.TestUserId, "HOME_IMPROVEMENT", 2000m, "USD");
        _factory.BudgetRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(budget);
        _factory.BudgetRepoMock
            .Setup(r => r.DeleteAsync(It.IsAny<Budget>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var response = await _client.DeleteAsync($"/api/v1/budgets/{budget.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // GET /api/v1/budgets/summary

    [Fact]
    public async Task GetSummary_NoAuth_Returns401()
    {
        var anon = _factory.CreateClient();
        var response = await anon.GetAsync("/api/v1/budgets/summary");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSummary_InvalidMonth_Returns400()
    {
        var response = await _client.GetAsync("/api/v1/budgets/summary?month=13");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorShape>();
        body!.ErrorCode.Should().Be("BUDGET_INVALID_PERIOD");
    }

    [Fact]
    public async Task GetSummary_Empty_Returns200WithShape()
    {
        _factory.BudgetRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Budget>());

        var response = await _client.GetAsync("/api/v1/budgets/summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BudgetSummaryShape>();
        body.Should().NotBeNull();
        body!.Items.Should().BeEmpty();
        body.TotalLimit.Should().Be(0m);
        body.TotalSpent.Should().Be(0m);
    }

    private record BudgetsListShape(List<object> Items, int TotalCount);
    private record BudgetDtoShape(Guid Id, string Category, string CategoryLabel, decimal MonthlyLimit, string Currency);
    private record BudgetSummaryShape(int Year, int Month, List<object> Items, decimal TotalLimit, decimal TotalSpent);
    private record ErrorShape(string ErrorCode, string Message);
}

public class BudgetsApiFactory : WebApplicationFactory<Program>
{
    public Mock<IBudgetRepository> BudgetRepoMock { get; } = new(MockBehavior.Loose);
    public Guid TestUserId { get; } = Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            ReplaceService(services, BudgetRepoMock.Object);

            ReplaceDbContextWithInMemory<FinanceSentry.Modules.Budgets.Infrastructure.Persistence.BudgetsDbContext>(
                services, $"BudgetsTest_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.BankSync.Infrastructure.Persistence.BankSyncDbContext>(
                services, $"BudgetsTestBankSync_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.Auth.Infrastructure.Persistence.AuthDbContext>(
                services, $"BudgetsTestAuth_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence.CryptoSyncDbContext>(
                services, $"BudgetsTestCrypto_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence.BrokerageSyncDbContext>(
                services, $"BudgetsTestBrokerage_{Guid.NewGuid()}");
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.Alerts.Infrastructure.Persistence.AlertsDbContext>(
                services, $"BudgetsTestAlerts_{Guid.NewGuid()}");

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
