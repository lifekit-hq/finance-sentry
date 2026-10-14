namespace FinanceSentry.Gateway.Tests;

using System.Net;
using Xunit;

/// <summary>
/// Rate-limit rejections must be readable (a bare 429 makes Safari download a zero-byte "start" file) and
/// the session bootstrap calls must not share the credential budget. Backends are unreachable in the
/// test host: an admitted request is a 502/503 from the proxy, a rejected one is a 429.
/// </summary>
public sealed class GatewayRateLimitTests(GatewayEndpointContractTests.GatewayFactory factory)
    : IClassFixture<GatewayEndpointContractTests.GatewayFactory>
{
    private const int CredentialPermit = 3;
    private const int SessionPermit = 5;
    private const string LoginPath = "/api/v1/auth/login";
    private const string StartPath = "/api/v1/auth/oidc/start";
    private const string MePath = "/api/v1/auth/me";
    private const string MethodsPath = "/api/v1/auth/methods";

    private HttpClient LimitedClient() => factory
        .WithWebHostBuilder(b => b
            .UseSetting("Gateway:RateLimits:Auth:PermitPerMinute", CredentialPermit.ToString())
            .UseSetting("Gateway:RateLimits:AuthSession:PermitPerMinute", SessionPermit.ToString()))
        .CreateClient();

    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string accept)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("Accept", accept);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> Exhaust(HttpClient client, HttpMethod method, string path, int permit)
    {
        for (var i = 0; i < permit; i++)
        {
            (await Send(client, method, path, "application/json")).Dispose();
        }

        return await Send(client, method, path, "application/json");
    }

    [Fact]
    public async Task Rejection_ForNavigation_IsHtmlWithRetryAfter()
    {
        var client = LimitedClient();
        await Exhaust(client, HttpMethod.Post, LoginPath, CredentialPermit);

        var response = await Send(client, HttpMethod.Post, LoginPath, "text/html,application/xhtml+xml;q=0.9,*/*;q=0.8");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("try again in a minute", await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    [Fact]
    public async Task Rejection_ForApiCall_IsProblemJsonWithRetryAfter()
    {
        var client = LimitedClient();

        var response = await Exhaust(client, HttpMethod.Post, LoginPath, CredentialPermit);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"status\":429", await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    [Fact]
    public async Task SessionEndpoints_DoNotShareTheCredentialBudget()
    {
        var client = LimitedClient();
        var exhausted = await Exhaust(client, HttpMethod.Post, LoginPath, CredentialPermit);
        Assert.Equal(HttpStatusCode.TooManyRequests, exhausted.StatusCode);

        foreach (var path in new[] { MePath, MethodsPath, StartPath })
        {
            var response = await Send(client, HttpMethod.Get, path, "application/json");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    [Fact]
    public async Task SessionEndpoints_ShareOneHigherBudget_AndLoginIsUnaffected()
    {
        var client = LimitedClient();
        var paths = new[] { MePath, MethodsPath, StartPath };
        for (var i = 0; i < SessionPermit; i++)
        {
            var ok = await Send(client, HttpMethod.Get, paths[i % paths.Length], "application/json");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, ok.StatusCode);
        }

        var rejected = await Send(client, HttpMethod.Get, StartPath, "text/html");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        var login = await Send(client, HttpMethod.Post, LoginPath, "application/json");
        Assert.NotEqual(HttpStatusCode.TooManyRequests, login.StatusCode);
    }

    [Fact]
    public async Task OtherAuthEndpoints_StayOnTheCredentialBudget()
    {
        var client = LimitedClient();

        // POST /auth/me is not the session read; it keeps the credential policy.
        var response = await Exhaust(client, HttpMethod.Post, MePath, CredentialPermit);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }
}
