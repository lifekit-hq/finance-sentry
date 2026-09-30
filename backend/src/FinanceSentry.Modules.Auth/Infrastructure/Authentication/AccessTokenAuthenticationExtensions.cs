namespace FinanceSentry.Modules.Auth.Infrastructure.Authentication;

using System.Text;
using FinanceSentry.Core.Api;
using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

/// <summary>
/// The authentication every HTTP host shares: the stock JwtBearer handler as the default scheme, validating
/// tokens signed with <c>Jwt:Secret</c> for one audience (<see cref="AuthAudiences"/>), plus a fallback policy
/// that requires an authenticated user on every endpoint not marked <see cref="AllowAnonymousAttribute"/>.
/// The request principal comes from the local account (<see cref="IAccessTokenPrincipalLoader"/>), so a
/// deleted or locked-out account's token stops working and roles reflect the current database state.
/// </summary>
public static class AccessTokenAuthenticationExtensions
{
    /// <summary>
    /// Registers authentication for tokens issued for <paramref name="audience"/> and the fallback policy.
    /// <paramref name="configure"/> runs after the shared options, for host-specific token sources, audience
    /// rules and events. Call after the Auth module's registration (Identity registers its own cookie scheme
    /// as the default; this replaces that default).
    /// </summary>
    public static IServiceCollection AddAccessTokenAuthentication(
        this IServiceCollection services,
        string audience,
        Action<JwtBearerOptions>? configure = null)
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
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        if (configure is not null)
            services.Configure(JwtBearerDefaults.AuthenticationScheme, configure);

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }

    /// <summary>
    /// Replaces the token's principal with the one built from the local account its <c>sub</c> names;
    /// fails authentication when that account is missing or locked out.
    /// </summary>
    public static async Task LoadAccountPrincipalAsync(TokenValidatedContext context)
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

    /// <summary>Answers a challenge with the API's JSON error body on 401.</summary>
    public static Task WriteUnauthorizedBodyAsync(JwtBearerChallengeContext context)
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
