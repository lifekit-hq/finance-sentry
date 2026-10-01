namespace FinanceSentry.Modules.Auth;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Infrastructure;
using FinanceSentry.Modules.Auth.Infrastructure.Authentication;
using FinanceSentry.Modules.Auth.Infrastructure.Authorization;
using FinanceSentry.Modules.Auth.Infrastructure.Identity;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using FinanceSentry.Modules.Auth.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class AuthModule
{
    /// <summary>How long an invite link (a password-reset token) stays valid.</summary>
    public static readonly TimeSpan InviteLifespan = TimeSpan.FromDays(7);

    internal sealed class ModuleRegistrar : IModuleRegistrar
    {
        public void Register(IServiceCollection services, IConfiguration config)
            => services.AddAuthModule(config);
    }

    public static IServiceCollection AddAuthModule(
        this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Default")!;

        services.AddDbContext<AuthDbContext>(o => o.UseNpgsql(connectionString, b => b.MigrationsHistoryTable("__EFMigrationsHistory", "public")));

        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                // Length plus a common-password check (CommonPasswordValidator), no composition rules.
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.Lockout.AllowedForNewUsers = true;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddDefaultTokenProviders()
            .AddPasswordValidator<CommonPasswordValidator>();

        // Reset tokens are only issued as invites (there is no self-service reset), so their lifespan is the invite's.
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = InviteLifespan);

        services.AddScoped<IAccessTokenPrincipalLoader, AccessTokenPrincipalLoader>();

        // One policy per permission: an authenticated user whose principal carries that permission claim.
        var authorization = services.AddAuthorizationBuilder();
        foreach (var (policyName, permission) in AuthPolicies.PermissionByPolicy)
        {
            authorization.AddPolicy(policyName, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(Permissions.ClaimType, permission));
        }

        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IMcpAuthorizationCodeStore, PersistedMcpAuthorizationCodeStore>();
        services.AddScoped<IMcpServiceTokenStore, PersistedMcpServiceTokenStore>();
        services.AddScoped<IMcpOAuthService, McpOAuthService>();

        services.Configure<GoogleOAuthOptions>(config.GetSection("GoogleOAuth"));
        services.AddScoped<IGoogleCredentialVerifier, GoogleCredentialVerifier>();

        services.AddScoped<IUserAlertPreferencesReader, UserAlertPreferencesReader>();
        services.AddScoped<IUserBaseCurrencyReader, UserBaseCurrencyReader>();
        services.AddScoped<IUserAuthorizationChecker, UserAuthorizationChecker>();
        services.AddScoped<IUserAccessService, UserAccessService>();
        services.AddScoped<IUserFireAssumptionsReader, UserFireAssumptionsReader>();

        return services;
    }
}
