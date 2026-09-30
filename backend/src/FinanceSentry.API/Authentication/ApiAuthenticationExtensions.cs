namespace FinanceSentry.API.Authentication;

using FinanceSentry.Core.Api;
using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.API.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

/// <summary>
/// The API host's authentication: the shared JwtBearer registration (<see cref="AccessTokenAuthenticationExtensions"/>)
/// for <see cref="AuthAudiences.App"/> tokens, reading the app's access token from the
/// <see cref="AuthCookies.AccessToken"/> cookie (<c>__Host-</c>-prefixed in production, see <see cref="AuthCookies"/>;
/// the Authorization header still works), plus the default-deny fallback policy.
/// </summary>
public static class ApiAuthenticationExtensions
{
    public const string ForbiddenCode = "FORBIDDEN";

    /// <summary>Registers authentication and the fallback policy. Call after <c>AddAllModules</c>
    /// (Identity registers its own cookie scheme as the default; this replaces that default).</summary>
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services) =>
        services.AddAccessTokenAuthentication(AuthAudiences.App, options =>
        {
            options.Events.OnMessageReceived = ReadAccessTokenCookie;
            options.Events.OnForbidden = WriteForbiddenBodyAsync;
            // Rollout: access tokens issued before the "app" audience existed carry no aud claim.
            // Accept those until they expire (one Jwt:ExpiryMinutes lifetime after deploy), then
            // delete this validator so ValidAudience alone applies. Any other audience is rejected.
            options.TokenValidationParameters.AudienceValidator = (audiences, _, _) =>
                !audiences.Any() || audiences.Contains(AuthAudiences.App);
        });

    private static Task ReadAccessTokenCookie(MessageReceivedContext context)
    {
        var secure = context.HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsProduction();
        var cookie = AuthCookies.Read(context.Request.Cookies, AuthCookies.AccessToken, secure);
        if (!string.IsNullOrWhiteSpace(cookie))
            context.Token = cookie;
        return Task.CompletedTask;
    }

    // Signed in but not permitted (the account lacks the endpoint's permission): 403 with the API's JSON error body.
    private static Task WriteForbiddenBodyAsync(ForbiddenContext context) =>
        context.Response.WriteAsJsonAsync(
            new ApiErrorBody("This feature is not available for your account.", ForbiddenCode));
}
