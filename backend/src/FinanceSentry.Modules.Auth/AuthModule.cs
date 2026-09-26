namespace FinanceSentry.Modules.Auth;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Infrastructure;
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
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddDefaultTokenProviders();

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
