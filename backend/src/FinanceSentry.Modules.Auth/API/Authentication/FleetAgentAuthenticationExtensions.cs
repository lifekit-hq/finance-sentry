namespace FinanceSentry.Modules.Auth.API.Authentication;

using System.Security.Claims;
using FinanceSentry.Core.Api;
using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Infrastructure.Authentication;
using FinanceSentry.Modules.Auth.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

/// <summary>
/// Accepts the fleet-agent identity next to the user scheme: a second, named JwtBearer scheme
/// (<see cref="FleetAgentOptions.Scheme"/>) that validates the org identity provider's machine-to-machine access
/// tokens (issuer, signing keys from its discovery document, the fleet audience, lifetime) and is never the default
/// scheme. It is reachable only through the fleet policies (<see cref="AuthPolicies.RequireFleetRead"/>,
/// <see cref="AuthPolicies.RequireFleetRecategorise"/>), which authenticate with it alone, so every other endpoint
/// still sees only the user scheme and a fleet token cannot reach it; a user's session token cannot pass the fleet policies.
/// <para>
/// The provider keeps issuing a token to a revoked or disabled application, but one without scope, so the scope is
/// checked, not only the token's validity: a token with none of the fleet scopes fails authentication. The request
/// principal is rebuilt, never taken from the token: the Owner account the fleet acts as
/// (<see cref="IFleetAgentAccountResolver"/>) as its user id, so the owner query filters scope reads and writes to
/// that person, and one <see cref="FleetScopes.ClaimType"/> claim per granted scope. It carries no role and no
/// permission claim, so no permission policy passes it.
/// </para>
/// </summary>
public static class FleetAgentAuthenticationExtensions
{
    private const string ScopeClaim = "scope";
    private const string ClientIdClaim = "client_id";
    private const string SubjectClaim = "sub";
    private static readonly string[] KnownScopes = [FleetScopes.Read, FleetScopes.Recategorise];

    public static IServiceCollection AddFleetAgentAuthentication(this IServiceCollection services)
    {
        services.AddOptions<FleetAgentOptions>().BindConfiguration(FleetAgentOptions.SectionName);
        services.AddScoped<IFleetAgentAccountResolver, FleetAgentAccountResolver>();

        services.AddAuthentication().AddJwtBearer(FleetAgentOptions.Scheme, options =>
        {
            options.MapInboundClaims = false;
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = RefuseWhenOff,
                OnTokenValidated = BuildFleetPrincipalAsync,
                OnChallenge = AccessTokenAuthenticationExtensions.WriteUnauthorizedBodyAsync,
                OnForbidden = context => context.Response.WriteAsJsonAsync(
                    new ApiErrorBody("This token's scope does not allow this.", "FORBIDDEN")),
            };
        });

        services.AddOptions<JwtBearerOptions>(FleetAgentOptions.Scheme)
            .Configure<IOptions<FleetAgentOptions>, IOptions<OidcLoginOptions>>(ConfigureValidation);

        var authorization = services.AddAuthorizationBuilder();
        foreach (var (policyName, scope) in AuthPolicies.ScopeByFleetPolicy)
        {
            authorization.AddPolicy(policyName, policy => policy
                .AddAuthenticationSchemes(FleetAgentOptions.Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(FleetScopes.ClaimType, scope));
        }

        return services;
    }

    private static void ConfigureValidation(JwtBearerOptions options, IOptions<FleetAgentOptions> fleet, IOptions<OidcLoginOptions> oidc)
    {
        var settings = fleet.Value;
        var authority = Pick(settings.Authority, oidc.Value.Authority);
        var backchannel = Pick(settings.BackchannelAuthority, oidc.Value.BackchannelAuthority);
        var backchannelIsPlainHttp = backchannel.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

        if (authority.Length > 0)
        {
            options.Authority = authority;
            if (backchannel.Length > 0)
                options.BackchannelHttpHandler = new OidcAuthorityRewriteHandler(authority, backchannel, new HttpClientHandler());
        }

        // Plain-HTTP metadata is allowed only on explicit request or when the back channel itself is plain HTTP.
        options.RequireHttpsMetadata = (settings.RequireHttpsMetadata ?? oidc.Value.RequireHttpsMetadata) && !backchannelIsPlainHttp;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authority,
            ValidateAudience = true,
            ValidAudience = settings.Audience,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = AccessTokenValidationParameters.ClockSkew,
        };
    }

    private static string Pick(string own, string fallback) => string.IsNullOrWhiteSpace(own) ? fallback ?? string.Empty : own;

    private static Task RefuseWhenOff(MessageReceivedContext context)
    {
        var fleet = context.HttpContext.RequestServices.GetRequiredService<IOptions<FleetAgentOptions>>().Value;
        var oidc = context.HttpContext.RequestServices.GetRequiredService<IOptions<OidcLoginOptions>>().Value;
        if (!fleet.Enabled || string.IsNullOrWhiteSpace(Pick(fleet.Authority, oidc.Authority)))
            context.Fail("The fleet-agent identity is not enabled.");

        return Task.CompletedTask;
    }

    private static async Task BuildFleetPrincipalAsync(TokenValidatedContext context)
    {
        var fleet = context.HttpContext.RequestServices.GetRequiredService<IOptions<FleetAgentOptions>>().Value;
        var token = context.Principal!;

        var clientId = token.FindFirst(ClientIdClaim)?.Value;
        var isMachineToken = !string.IsNullOrEmpty(clientId) && token.FindFirst(SubjectClaim)?.Value == clientId;
        if (!isMachineToken || (fleet.ClientId.Length > 0 && clientId != fleet.ClientId))
        {
            context.Fail("The token was not issued to the fleet-agent application.");
            return;
        }

        var granted = KnownScopes.Intersect(
            token.FindAll(ScopeClaim).SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)),
            StringComparer.Ordinal).ToList();
        if (granted.Count == 0)
        {
            context.Fail("The token carries no fleet scope (the application is revoked or disabled).");
            return;
        }

        var userId = await context.HttpContext.RequestServices.GetRequiredService<IFleetAgentAccountResolver>()
            .ResolveAsync(context.HttpContext.RequestAborted);
        if (userId is null)
        {
            context.Fail("The fleet-agent identity has no active Owner account to act as.");
            return;
        }

        var identity = new ClaimsIdentity(FleetAgentOptions.Scheme, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Name, FleetAgentOptions.Scheme));
        identity.AddClaim(new Claim(ClientIdClaim, clientId!));
        identity.AddClaims(granted.Select(scope => new Claim(FleetScopes.ClaimType, scope)));
        context.Principal = new ClaimsPrincipal(identity);
    }
}
