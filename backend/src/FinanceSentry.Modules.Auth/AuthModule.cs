namespace FinanceSentry.Modules.Auth;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Auth.API.Authentication;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Infrastructure;
using FinanceSentry.Modules.Auth.Infrastructure.Authentication;
using FinanceSentry.Modules.Auth.Infrastructure.Authorization;
using FinanceSentry.Modules.Auth.Infrastructure.Jobs;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using FinanceSentry.Modules.Auth.Infrastructure.Services;
using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using FinanceSentry.Infrastructure.Persistence;

public static class AuthModule
{
    internal sealed class ModuleRegistrar : IModuleRegistrar
    {
        public void Register(IServiceCollection services, IConfiguration config)
            => services.AddAuthModule(config);
    }

    private sealed class JobRegistrar : IJobRegistrar
    {
        // Daily, 03:30 UTC: erases the data of closed accounts whose grace period has ended.
        public void RegisterJobs(IServiceProvider sp)
            => sp.GetRequiredService<IRecurringJobManager>().AddOrUpdate<AccountPurgeJob>(
                AccountPurgeJob.JobId,
                job => job.ExecuteAsync(CancellationToken.None),
                "30 3 * * *");
    }

    public static IServiceCollection AddAuthModule(
        this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Default")!;

        services.AddUpdatedAtStamping();
        services.AddDbContext<AuthDbContext>((sp, o) => o.UseNpgsql(connectionString, b => b.MigrationsHistoryTable("__EFMigrationsHistory", "public")).UseUpdatedAtStamping(sp));
        services.AddScoped<IUserDataEraser, AuthUserDataEraser>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<AccountPurgeJob>();
        services.AddSingleton<IJobRegistrar, JobRegistrar>();

        services.AddAuthDataProtection(config);

        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.Lockout.AllowedForNewUsers = true;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<AuthDbContext>();

        services.AddScoped<IAccessTokenPrincipalLoader, AccessTokenPrincipalLoader>();

        // One policy per permission: an authenticated user whose principal carries that permission claim.
        var authorization = services.AddAuthorizationBuilder();
        foreach (var (policyName, permission) in AuthPolicies.PermissionByPolicy)
        {
            authorization.AddPolicy(policyName, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(Permissions.ClaimType, permission));
        }

        services.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.SectionName);
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IMcpAuthorizationCodeStore, PersistedMcpAuthorizationCodeStore>();
        services.AddScoped<IMcpServiceTokenStore, PersistedMcpServiceTokenStore>();
        services.AddScoped<IMcpOAuthService, McpOAuthService>();

        services.Configure<AuthSignInOptions>(config.GetSection(AuthSignInOptions.SectionName));
        services.AddOidcLogin(config);

        services.AddScoped<IUserAlertPreferencesReader, UserAlertPreferencesReader>();
        services.AddScoped<IUserBaseCurrencyReader, UserBaseCurrencyReader>();
        services.AddScoped<IClosedAccountsReader, ClosedAccountsReader>();
        services.AddScoped<IUserAuthorizationChecker, UserAuthorizationChecker>();
        services.AddScoped<IOrgIdentityLinkReader, OrgIdentityLinkReader>();
        services.AddScoped<IUserAccessService, UserAccessService>();
        services.AddScoped<IUserFireAssumptionsReader, UserFireAssumptionsReader>();

        return services;
    }
}
