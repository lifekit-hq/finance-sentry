using System.Net.Http.Json;
using FinanceSentry.Infrastructure.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FinanceSentry.Mcp.Abstractions;

/// <summary>
/// Calls the API's MCP token endpoints. The singleton hosts (<see cref="LocalMcpSession"/>) hold it for the
/// process lifetime, so it takes <see cref="IHttpClientFactory"/> and asks for a client per call - a typed
/// <see cref="HttpClient"/> captured by a singleton would pin one handler and never see DNS changes.
/// </summary>
public sealed class McpOAuthTokenClient(IHttpClientFactory httpFactory, ILogger<McpOAuthTokenClient> logger)
{
    public const string HttpClientName = "mcp-oauth-token";

    public const string McpScope = "mcp.full_access";

    public async Task<StoredMcpCredentials> ExchangeAuthorizationCodeAsync(
        string apiBaseUrl,
        string frontendBaseUrl,
        string code,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        var http = httpFactory.CreateClient(HttpClientName);
        using var response = await http.PostAsJsonAsync(
            $"{apiBaseUrl.TrimEnd('/')}/auth/mcp/token",
            new { grant_type = "authorization_code", code, redirect_uri = redirectUri },
            cancellationToken);

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<McpTokenPayload>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("MCP token response was empty.");

        return payload.ToStored(apiBaseUrl, frontendBaseUrl);
    }

    public async Task<StoredMcpCredentials> RefreshAsync(
        StoredMcpCredentials current,
        CancellationToken cancellationToken = default)
    {
        var http = httpFactory.CreateClient(HttpClientName);
        using var response = await http.PostAsJsonAsync(
            $"{current.ApiBaseUrl.TrimEnd('/')}/auth/mcp/token",
            new { grant_type = "refresh_token", refresh_token = current.RefreshToken },
            cancellationToken);

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<McpTokenPayload>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("MCP refresh response was empty.");

        return payload.ToStored(current.ApiBaseUrl, current.FrontendBaseUrl);
    }

    public async Task RevokeAsync(StoredMcpCredentials current, CancellationToken cancellationToken = default)
    {
        try
        {
            var http = httpFactory.CreateClient(HttpClientName);
            using var response = await http.PostAsJsonAsync(
                $"{current.ApiBaseUrl.TrimEnd('/')}/auth/mcp/revoke",
                new { refresh_token = current.RefreshToken },
                cancellationToken);

            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to revoke MCP refresh token cleanly.");
        }
    }

    private sealed record McpTokenPayload(
        string AccessToken,
        string RefreshToken,
        string TokenType,
        int ExpiresIn,
        DateTime ExpiresAt,
        string UserId,
        string Email,
        string Scope)
    {
        public StoredMcpCredentials ToStored(string apiBaseUrl, string frontendBaseUrl)
            => new(
                apiBaseUrl,
                frontendBaseUrl,
                AccessToken,
                RefreshToken,
                ExpiresAt,
                UserId,
                Email,
                Scope);
    }
}

public static class McpOAuthTokenClientRegistration
{
    /// <summary>
    /// Registers <see cref="McpOAuthTokenClient"/> over a named client with the platform resilience handler.
    /// Both calls are POSTs - an authorization code and a rotating refresh token are single use - so nothing
    /// is retried; the handler contributes the timeouts, circuit breaker and telemetry.
    /// </summary>
    public static IServiceCollection AddMcpOAuthTokenClient(this IServiceCollection services)
    {
        services.AddHttpClient(McpOAuthTokenClient.HttpClientName)
            .AddProviderResilience(ProviderHttpResilience.DefaultTimeout, maxRetries: 0);
        services.AddSingleton<McpOAuthTokenClient>();
        return services;
    }
}
