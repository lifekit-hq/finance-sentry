namespace FinanceSentry.Modules.Auth.API.Authentication;

using Microsoft.Extensions.Options;

/// <summary>
/// Startup validation for <see cref="JwtOptions"/>: a missing or blank <c>Jwt:Secret</c> stops the host coming
/// up (paired with <c>ValidateOnStart()</c>) instead of failing the first request that touches authentication.
/// Registered by <see cref="AccessTokenAuthenticationExtensions.AddAccessTokenAuthentication"/>, so only the
/// hosts that validate tokens require the secret; the host-run stdio MCP server keeps it optional.
/// </summary>
public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    internal const string SecretNotConfigured =
        "Jwt:Secret is not configured. Set Jwt__Secret (the HMAC-SHA256 key access tokens are signed "
        + "with) in the environment or a secrets store.";

    public ValidateOptionsResult Validate(string? name, JwtOptions options) =>
        options.HasSecret ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(SecretNotConfigured);
}
