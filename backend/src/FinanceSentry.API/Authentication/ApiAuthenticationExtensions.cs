namespace FinanceSentry.API.Authentication;

using System.Text;
using FinanceSentry.Core.Api;
using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

/// <summary>
/// The API host's authentication: the stock JwtBearer handler as the default scheme, reading the app's
/// access token from the <see cref="AccessTokenCookie"/> cookie (the Authorization header still works),
/// plus a fallback policy that requires an authenticated user on every endpoint not marked
/// <see cref="AllowAnonymousAttribute"/>. The request principal comes from the local account, so a
/// deleted or locked-out account's token stops working and roles reflect the current database state.
/// </summary>
public static class ApiAuthenticationExtensions
{
    public const string AccessTokenCookie = "fs_access_token";

    /// <summary>Registers authentication and the fallback policy. Call after <c>AddAllModules</c>
    /// (Identity registers its own cookie scheme as the default; this replaces that default).</summary>
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ReadAccessTokenCookie,
                    OnTokenValidated = LoadAccountPrincipalAsync,
                    OnChallenge = WriteUnauthorizedBodyAsync,
                };
            });

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IConfiguration>((options, config) =>
            {
                var secret = config["Jwt:Secret"]
                    ?? throw new InvalidOperationException("Jwt:Secret is not configured.");

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateIssuer = false,
                    ValidateAudience = true,
                    ValidAudience = AuthAudiences.App,
                    // Rollout: access tokens issued before the "app" audience existed carry no aud claim.
                    // Accept those until they expire (one Jwt:ExpiryMinutes lifetime after deploy), then
                    // delete this validator so ValidAudience alone applies. Any other audience is rejected.
                    AudienceValidator = (audiences, _, _) =>
                        !audiences.Any() || audiences.Contains(AuthAudiences.App),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }

    private static Task ReadAccessTokenCookie(MessageReceivedContext context)
    {
        var cookie = context.Request.Cookies[AccessTokenCookie];
        if (!string.IsNullOrWhiteSpace(cookie))
            context.Token = cookie;
        return Task.CompletedTask;
    }

    private static async Task LoadAccountPrincipalAsync(TokenValidatedContext context)
    {
        var userId = context.Principal?.GetUserId();
        var principal = userId is null
            ? null
            : await context.HttpContext.RequestServices.GetRequiredService<IAccessTokenPrincipalLoader>()
                .LoadAsync(userId.Value.ToString());

        if (principal is null)
        {
            context.Fail("The token's account does not exist or is locked out.");
            return;
        }

        context.Principal = principal;
    }

    // Keeps the API's JSON error body on 401 (the frontend reads only the status).
    private static Task WriteUnauthorizedBodyAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;

        var body = context.AuthenticateFailure switch
        {
            null => new ApiErrorBody("Authentication required.", "UNAUTHORIZED"),
            SecurityTokenExpiredException => new ApiErrorBody("Token has expired. Please sign in again.", "TOKEN_EXPIRED"),
            _ => new ApiErrorBody("Invalid authentication token.", "TOKEN_INVALID"),
        };
        return context.Response.WriteAsJsonAsync(body);
    }
}
