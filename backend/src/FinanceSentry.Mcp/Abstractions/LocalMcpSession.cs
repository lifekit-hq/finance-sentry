using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FinanceSentry.Modules.Auth.API.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FinanceSentry.Mcp.Abstractions;

public sealed class LocalMcpSession(
    LocalMcpCredentialStore credentialStore,
    McpOAuthTokenClient tokenClient,
    IOptions<JwtOptions> jwtOptions,
    ILogger<LocalMcpSession> logger)
{
    private const string ExpectedAudience = "mcp";
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(2);
    private readonly object _sync = new();
    private ClaimsPrincipal? _cachedPrincipal;
    private DateTime _cachedExpiresAt;

    public ClaimsPrincipal? GetPrincipal()
    {
        lock (_sync)
        {
            return _cachedExpiresAt > DateTime.UtcNow.Add(RefreshSkew)
                ? _cachedPrincipal
                : null;
        }
    }

    public void Invalidate()
    {
        lock (_sync)
        {
            _cachedPrincipal = null;
            _cachedExpiresAt = DateTime.MinValue;
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RefreshIfNeededAsync(forceValidation: true, cancellationToken);
    }

    public async Task RefreshIfNeededAsync(bool forceValidation = false, CancellationToken cancellationToken = default)
    {
        var credentials = credentialStore.Load();
        if (credentials is null)
        {
            Invalidate();
            return;
        }

        if (credentials.ExpiresAt <= DateTime.UtcNow.Add(RefreshSkew))
        {
            credentials = await TryRefreshAsync(credentials, cancellationToken);
            if (credentials is null)
            {
                Invalidate();
                return;
            }
        }

        if (!forceValidation && TryUseCached(credentials.ExpiresAt))
            return;

        var principal = Validate(credentials.AccessToken);
        if (principal is not null)
        {
            Cache(principal, credentials.ExpiresAt);
            return;
        }

        credentials = await TryRefreshAsync(credentials, cancellationToken);
        if (credentials is null)
        {
            Invalidate();
            return;
        }

        principal = Validate(credentials.AccessToken);
        if (principal is null)
        {
            Invalidate();
            return;
        }

        Cache(principal, credentials.ExpiresAt);
    }

    private bool TryUseCached(DateTime expiresAt)
    {
        lock (_sync)
        {
            return _cachedPrincipal is not null && _cachedExpiresAt == expiresAt;
        }
    }

    private void Cache(ClaimsPrincipal principal, DateTime expiresAt)
    {
        lock (_sync)
        {
            _cachedPrincipal = principal;
            _cachedExpiresAt = expiresAt;
        }
    }

    private async Task<StoredMcpCredentials?> TryRefreshAsync(
        StoredMcpCredentials credentials,
        CancellationToken cancellationToken)
    {
        try
        {
            var refreshed = await tokenClient.RefreshAsync(credentials, cancellationToken);
            credentialStore.Save(refreshed);
            return refreshed;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to refresh stored MCP credentials.");
            return null;
        }
    }

    private ClaimsPrincipal? Validate(string token)
    {
        // Optional here: the host-run stdio server may run without the signing key, which makes the stored
        // credentials unverifiable (no principal) rather than a startup failure.
        if (!jwtOptions.Value.HasSecret)
            return null;

        var handler = new JwtSecurityTokenHandler();
        var parameters = AccessTokenValidationParameters.Create(jwtOptions.Value.Secret!, ExpectedAudience);

        try
        {
            return handler.ValidateToken(token, parameters, out _);
        }
        catch (SecurityTokenException ex)
        {
            logger.LogWarning(ex, "Stored MCP access token failed validation.");
            return null;
        }
    }
}
