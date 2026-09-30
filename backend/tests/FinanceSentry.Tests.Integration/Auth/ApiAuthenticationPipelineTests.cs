namespace FinanceSentry.Tests.Integration.Auth;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

/// <summary>
/// The API host's authentication pipeline: stock JwtBearer over the access-token cookie, a fallback policy
/// requiring an authenticated user, and a reviewed set of endpoints that allow anonymous access.
/// </summary>
public class ApiAuthenticationPipelineTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string Password = "TestPass123!";
    private const string ProtectedPath = "/api/v1/profile";
    private const string JwtSecret = "test-jwt-secret-key-for-integration-tests-minimum-32-chars";

    /// <summary>Every endpoint that allows anonymous access, as "METHOD route" (* = any method).</summary>
    private static readonly string[] ReviewedAnonymousEndpoints =
    [
        "* /api/v1/health/ready",
        "* /metrics",
        "GET /api/v1/Health",
        "GET /api/v1/accounts/truelayer/callback",
        "GET /api/v1/auth/mcp/authorize",
        "GET /api/v1/auth/me",
        "POST /api/v1/auth/google/verify",
        "POST /api/v1/auth/login",
        "POST /api/v1/auth/logout",
        "POST /api/v1/auth/mcp/revoke",
        "POST /api/v1/auth/mcp/token",
        "POST /api/v1/auth/refresh",
        "POST /api/v1/auth/register",
    ];

    [Fact]
    public void AnonymousAccess_IsAllowedOnExactlyTheReviewedEndpoints()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();

        var anonymous = endpoints
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .SelectMany(Describe)
            .Distinct()
            .Order(StringComparer.Ordinal);

        anonymous.Should().Equal(ReviewedAnonymousEndpoints);
    }

    [Fact]
    public async Task FallbackPolicy_RequiresAnAuthenticatedUser()
    {
        var policies = factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var fallback = await policies.GetFallbackPolicyAsync();

        fallback.Should().NotBeNull();
        fallback!.Requirements.Should().ContainSingle(r => r is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401()
    {
        var response = await Client().GetAsync(ProtectedPath);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<ErrorResponseShape>())!.ErrorCode.Should().Be("UNAUTHORIZED");
    }

    [Fact]
    public async Task ProtectedEndpoint_WithAppTokenFromLogin_Returns200()
    {
        var token = await SignInAsync("pipeline-app-token@test.com");

        var response = await Client(token).GetAsync(ProtectedPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithAppTokenInAuthorizationHeader_Returns200()
    {
        var token = await SignInAsync("pipeline-bearer-header@test.com");
        using var client = Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var response = await client.GetAsync(ProtectedPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithTokenWithoutAudience_Returns200DuringRollout()
    {
        var user = await CreateUserAsync("pipeline-no-audience@test.com");

        var response = await Client(MintToken(user.Id, audience: null)).GetAsync(ProtectedPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithMcpAudienceToken_Returns401()
    {
        var user = await CreateUserAsync("pipeline-mcp-audience@test.com");
        using var scope = factory.Services.CreateScope();
        var (mcpToken, _) = scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateMcpAccessToken(user);

        var response = await Client(mcpToken).GetAsync(ProtectedPath);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithTokenForUnknownAccount_Returns401()
    {
        var response = await Client(MintToken(Guid.NewGuid().ToString(), audience: "app")).GetAsync(ProtectedPath);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithTokenOfLockedOutAccount_Returns401()
    {
        var user = await CreateUserAsync("pipeline-locked-out@test.com");
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var stored = await users.FindByIdAsync(user.Id);
            await users.SetLockoutEndDateAsync(stored!, DateTimeOffset.UtcNow.AddHours(1));
        }

        var response = await Client(MintToken(user.Id, audience: "app")).GetAsync(ProtectedPath);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_AfterRepeatedFailures_LocksTheAccount()
    {
        const string email = "pipeline-lockout@test.com";
        await factory.EnsureUserExistsAsync(email, Password);
        using var client = Client();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failed = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "WrongPassword!" });
            failed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var withCorrectPassword = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });

        withCorrectPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await withCorrectPassword.Content.ReadFromJsonAsync<ErrorResponseShape>())!.ErrorCode.Should().Be("INVALID_CREDENTIALS");
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await users.IsLockedOutAsync((await users.FindByEmailAsync(email))!)).Should().BeTrue();
    }

    [Fact]
    public async Task Register_WithCommonPassword_Returns400()
    {
        var response = await Client().PostAsJsonAsync("/api/v1/auth/register",
            new { email = "pipeline-common-password@test.com", password = "Password1" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponseShape>();
        body!.ErrorCode.Should().Be("VALIDATION_ERROR");
        body.Details.Should().Contain(CommonPasswordValidator.ErrorDescription);
    }

    [Fact]
    public async Task Register_WithLongPasswordWithoutCompositionRules_Succeeds()
    {
        var response = await Client().PostAsJsonAsync("/api/v1/auth/register",
            new { email = "pipeline-passphrase@test.com", password = "quiet lantern orchard" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static IEnumerable<string> Describe(RouteEndpoint endpoint)
    {
        var route = "/" + endpoint.RoutePattern.RawText!.TrimStart('/');
        var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
        return methods is { Count: > 0 } ? methods.Select(m => $"{m} {route}") : [$"* {route}"];
    }

    private HttpClient Client(string? accessToken = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        if (accessToken is not null)
            client.DefaultRequestHeaders.Add("Cookie", $"fs_access_token={accessToken}");
        return client;
    }

    private async Task<ApplicationUser> CreateUserAsync(string email)
    {
        await factory.EnsureUserExistsAsync(email, Password);
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!;
    }

    private async Task<string> SignInAsync(string email)
    {
        await factory.EnsureUserExistsAsync(email, Password);
        using var client = Client();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        const string prefix = "fs_access_token=";
        var cookie = login.Headers.GetValues("Set-Cookie").First(c => c.StartsWith(prefix, StringComparison.Ordinal));
        return cookie[prefix.Length..].Split(';')[0];
    }

    private static string MintToken(string userId, string? audience)
    {
        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("sub", userId)]),
            Audience = audience,
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)), SecurityAlgorithms.HmacSha256),
        });
        return handler.WriteToken(token);
    }

    private sealed record ErrorResponseShape(string Error, string ErrorCode, IReadOnlyList<string>? Details);
}
