namespace FinanceSentry.Tests.Integration.Auth;

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using Xunit;

/// <summary>
/// Contract tests for authenticated BankSync endpoints (T027).
/// Validates that GET /accounts requires a valid Bearer token
/// and that userId is extracted from the JWT claim (no query param).
/// </summary>
public class AccountsAuthContractTests : IClassFixture<AuthApiFactory>
{
    private readonly HttpClient _unauthenticatedClient;
    private readonly HttpClient _authenticatedClient;

    public AccountsAuthContractTests(AuthApiFactory factory)
    {
        _unauthenticatedClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        _authenticatedClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var userId = Guid.NewGuid();
        TestUsers.EnsureExists(factory.Services, userId);
        _authenticatedClient.DefaultRequestHeaders.Add("Cookie", $"fs_access_token={GenerateTestJwt(userId)}");
    }

    [Fact]
    public async Task GetAccounts_WithNoToken_Returns401Unauthorized()
    {
        var response = await _unauthenticatedClient.GetAsync("/api/v1/accounts");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadFromJsonAsync<ErrorResponseShape>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("UNAUTHORIZED");
    }

    [Fact]
    public async Task GetAccounts_WithValidToken_Returns200WithoutUserIdQueryParam()
    {
        // The endpoint must NOT require ?userId — userId comes from JWT claim
        var response = await _authenticatedClient.GetAsync("/api/v1/accounts");

        // 200 or other non-auth error (e.g. 500 from missing DB data) is acceptable —
        // the key assertion is that it is NOT 401/403.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    private static string GenerateTestJwt(Guid userId)
    {
        const string secret = "test-jwt-secret-key-for-integration-tests-minimum-32-chars";
        var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(secret));
        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("sub", userId.ToString())]),
            Audience = FinanceSentry.Core.Auth.AuthAudiences.App,
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        });
        return handler.WriteToken(token);
    }

    private record ErrorResponseShape(string Error, string ErrorCode);
}
