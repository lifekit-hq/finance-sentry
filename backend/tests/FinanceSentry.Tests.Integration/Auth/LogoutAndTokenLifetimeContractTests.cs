namespace FinanceSentry.Tests.Integration.Auth;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

/// <summary>
/// Regression coverage for two independently-shipped bugs found during the guard-2 edge
/// identity review:
/// - Logout must revoke the refresh token identified by the request's own refresh cookie
///   (POST /api/v1/auth is exempt from JwtAuthenticationMiddleware, so `User` is never
///   populated there).
/// - The access token lifetime must come from Jwt:ExpiryMinutes rather than a hardcoded 60.
/// </summary>
public class LogoutAndTokenLifetimeContractTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public LogoutAndTokenLifetimeContractTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Logout_RevokesRefreshTokenFromCookie_SoItNoLongerRefreshes()
    {
        await _factory.EnsureUserExistsAsync("logout-revokes@test.com", "TestPass123!");

        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "logout-revokes@test.com", password = "TestPass123!" });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshCookie = ExtractCookieValue(loginResponse, "fs_refresh_token");
        refreshCookie.Should().NotBeNullOrEmpty();

        var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logoutRequest.Headers.Add("Cookie", $"fs_refresh_token={refreshCookie}");
        var logoutResponse = await _client.SendAsync(logoutRequest);
        logoutResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The cookie that logout was called with must no longer refresh a session —
        // this is the end-user-visible symptom of the bug (logout never revoked it).
        var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        refreshRequest.Headers.Add("Cookie", $"fs_refresh_token={refreshCookie}");
        var refreshResponse = await _client.SendAsync(refreshRequest);
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_WithNoRefreshCookie_StillClearsBothCookiesAndReturnsNoContent()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.TryGetValues("Set-Cookie", out var cookies);
        cookies.Should().Contain(c => c.StartsWith("fs_refresh_token="));
        cookies.Should().Contain(c => c.StartsWith("fs_access_token="));
    }

    [Fact]
    public async Task Login_RespectsConfiguredExpiryMinutes_InsteadOfHardcoded60()
    {
        await _factory.EnsureUserExistsAsync("expiry-check@test.com", "TestPass123!");

        await using var customFactory = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Jwt:ExpiryMinutes", "5"));
        var customClient = customFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await customClient.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "expiry-check@test.com", password = "TestPass123!" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AuthResponseShape>();
        body.Should().NotBeNull();

        // Hardcoding AddMinutes(60) in the handler would put this ~55 minutes late.
        body!.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(30));
    }

    private static string? ExtractCookieValue(HttpResponseMessage response, string cookieName)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
            return null;

        foreach (var cookie in cookies)
        {
            if (!cookie.StartsWith($"{cookieName}=", StringComparison.Ordinal))
                continue;

            var afterName = cookie[(cookieName.Length + 1)..];
            var separatorIndex = afterName.IndexOf(';');
            return separatorIndex >= 0 ? afterName[..separatorIndex] : afterName;
        }

        return null;
    }

    private record UserShape(string Id, string Email);
    private record AuthResponseShape(UserShape User, DateTime ExpiresAt);
}
