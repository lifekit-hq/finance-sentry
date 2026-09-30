namespace FinanceSentry.Modules.Auth;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Infrastructure;
using FinanceSentry.Modules.Auth.Infrastructure.Authentication;
using FinanceSentry.Modules.Auth.Infrastructure.Identity;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using FinanceSentry.Modules.Auth.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class AuthModule
{
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

        services.AddMemoryCache();
        services.AddScoped<IAccessTokenPrincipalLoader, AccessTokenPrincipalLoader>();

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.RequireOwner, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(AuthRoles.Owner));

        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IMcpAuthorizationCodeStore, PersistedMcpAuthorizationCodeStore>();
        services.AddScoped<IMcpServiceTokenStore, PersistedMcpServiceTokenStore>();
        services.AddScoped<IMcpOAuthService, McpOAuthService>();

        services.Configure<GoogleOAuthOptions>(config.GetSection("GoogleOAuth"));
        services.AddScoped<IGoogleCredentialVerifier, GoogleCredentialVerifier>();

        services.AddScoped<IUserAlertPreferencesReader, UserAlertPreferencesReader>();
        services.AddScoped<IUserBaseCurrencyReader, UserBaseCurrencyReader>();
        services.AddScoped<IUserFireAssumptionsReader, UserFireAssumptionsReader>();

        return services;
    }
}
