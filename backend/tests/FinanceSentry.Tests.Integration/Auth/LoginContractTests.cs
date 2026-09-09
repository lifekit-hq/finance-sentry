namespace FinanceSentry.Tests.Integration.Auth;

using System.Net;
using System.Net.Http.Json;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using Moq;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

/// <summary>
/// REST API contract tests for POST /api/v1/auth/login (T013).
/// Validates response shapes and status codes per contracts/auth-api.md.
/// </summary>
public class LoginContractTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public LoginContractTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Login_HappyPath_Returns200WithAuthResponseSchema()
    {
        await _factory.EnsureUserExistsAsync("login-happy@test.com", "TestPass123!");

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "login-happy@test.com", password = "TestPass123!" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<AuthResponseShape>();
        body.Should().NotBeNull();
        body!.User.Should().NotBeNull();
        body.User.Id.Should().NotBeNullOrWhiteSpace();
        Guid.TryParse(body.User.Id, out _).Should().BeTrue("UserId must be a valid GUID");
        body.ExpiresAt.Should().BeAfter(DateTime.UtcNow);

        response.Headers.TryGetValues("Set-Cookie", out var cookies);
        cookies.Should().Contain(c => c.StartsWith("fs_access_token="), "login must set the access token cookie");
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401WithInvalidCredentials()
    {
        await _factory.EnsureUserExistsAsync("login-wrong@test.com", "TestPass123!");

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "login-wrong@test.com", password = "WrongPassword!" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadFromJsonAsync<ErrorResponseShape>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Login_MissingEmail_Returns400WithValidationError()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "", password = "TestPass123!" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<ErrorResponseShape>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task Login_MissingPassword_Returns400WithValidationError()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "login-nopw@test.com", password = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<ErrorResponseShape>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    private record UserShape(string Id, string Email);
    private record AuthResponseShape(UserShape User, DateTime ExpiresAt);
    private record ErrorResponseShape(string Error, string ErrorCode);
}

/// <summary>
/// WebApplicationFactory for Auth contract tests.
/// Replaces real databases with in-memory equivalents and seeds test data.
/// </summary>
public class AuthApiFactory : WebApplicationFactory<Program>
{
    private static readonly InMemoryDatabaseRoot _bankSyncDbRoot = new();
    private static readonly InMemoryDatabaseRoot _authDbRoot = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            ReplaceWithInMemory<BankSyncDbContext>(services, "auth-contract-banksync", _bankSyncDbRoot);
            ReplaceWithInMemory<AuthDbContext>(services, "auth-contract-auth", _authDbRoot);

            services.Configure<FinanceSentry.Infrastructure.Encryption.EncryptionOptions>(opts =>
            {
                opts.CurrentKeyVersion = 1;
                opts.Keys = new Dictionary<int, string>
                {
                    [1] = "dGVzdC1vbmx5LWtleS1ub3QtdGhlLWxlYWtlZC1vbmU="
                };
            });

            var mockVerifier = new Mock<IGoogleCredentialVerifier>();
            mockVerifier
                .Setup(v => v.VerifyAsync("valid-test-credential"))
                .ReturnsAsync(new GoogleUserInfo("google-sub-123", "google@test.com", "Test User"));
            mockVerifier
                .Setup(v => v.VerifyAsync("new-user-credential"))
                .ReturnsAsync(new GoogleUserInfo("google-sub-new", "newgoogle@test.com", "New User"));
            mockVerifier
                .Setup(v => v.VerifyAsync("link-credential"))
                .ReturnsAsync(new GoogleUserInfo("google-sub-link", "link@test.com", "Link User"));
            mockVerifier
                .Setup(v => v.VerifyAsync("invalid-credential"))
                .ThrowsAsync(new InvalidGoogleCredentialException());
            services.RemoveAll<IGoogleCredentialVerifier>();
            services.AddScoped(_ => mockVerifier.Object);
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
        builder.UseSetting("Jwt:ExpiryMinutes", "60");
        builder.UseSetting("GoogleOAuth:ClientId", "test-client-id");
    }

    public async Task EnsureUserExistsAsync(string email, string password)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (await userManager.FindByEmailAsync(email) is null)
        {
            var user = new ApplicationUser { UserName = email, Email = email };
            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    $"Failed to seed test user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }
    }

    private static void ReplaceWithInMemory<TContext>(
        IServiceCollection services,
        string dbName,
        InMemoryDatabaseRoot root)
        where TContext : DbContext
    {
        // EF9: must remove ALL provider-related registrations to avoid multiple-provider conflict.
        // Remove the context, its options, and any IDbContextOptionsConfiguration<TContext>
        // (which carries the provider selection) before re-registering with InMemory.
        var toRemove = services
            .Where(d => d.ServiceType == typeof(DbContextOptions<TContext>)
                     || d.ServiceType == typeof(TContext)
                     || d.ServiceType == typeof(IDbContextOptionsConfiguration<TContext>))
            .ToList();
        foreach (var d in toRemove)
            services.Remove(d);

        services.AddDbContext<TContext>(options =>
            options.UseInMemoryDatabase(dbName, root));
    }
}
