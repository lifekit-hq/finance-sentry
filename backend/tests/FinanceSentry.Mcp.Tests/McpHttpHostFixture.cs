using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

/// <summary>
/// Boots the MCP HTTP host (<see cref="McpHttpHost"/>) on a test server with the Auth module's store in
/// memory; every other module keeps an unreachable Postgres, which listing tools never touches.
/// </summary>
public sealed class McpHttpHostFixture : IAsyncLifetime
{
    public const string JwtSecret = "test-jwt-secret-key-for-mcp-pipeline-tests-minimum-32-chars";

    private const string UnreachableConnectionString = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1";

    private readonly InMemoryDatabaseRoot _authDbRoot = new();
    private WebApplication? _app;

    public IServiceProvider Services => _app!.Services;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = UnreachableConnectionString,
            ["ConnectionStrings:ReadOnly"] = UnreachableConnectionString,
            ["Deduplication:MasterKeyBase64"] = "MDEyMzQ1Njc4OUFCQ0RFRg==",
            ["Jwt:Secret"] = JwtSecret,
        });

        McpHttpHost.AddServices(builder.Services, builder.Configuration);
        ReplaceAuthStoreWithInMemory(builder.Services);

        _app = builder.Build();
        McpHttpHost.UsePipeline(_app);
        await _app.StartAsync();
        Client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        if (_app is not null)
            await _app.DisposeAsync();
    }

    public async Task<ApplicationUser> CreateUserAsync()
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        var user = new ApplicationUser { UserName = email, Email = email };
        await WithUsersAsync(async users =>
        {
            var result = await users.CreateAsync(user);
            result.Succeeded.Should().BeTrue();
        });
        return user;
    }

    public async Task WithUsersAsync(Func<UserManager<ApplicationUser>, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
    }

    public async Task WithServiceTokenStoreAsync(Func<IMcpServiceTokenStore, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<IMcpServiceTokenStore>());
    }

    public T Tokens<T>(Func<ITokenService, T> issue)
    {
        using var scope = Services.CreateScope();
        return issue(scope.ServiceProvider.GetRequiredService<ITokenService>());
    }

    /// <summary>Issues a service token and stores its jti, as the service-token endpoint does.</summary>
    public async Task<(string Token, Guid Jti)> IssueServiceTokenAsync(ApplicationUser user)
    {
        var (token, jti, expiresAt) = Tokens(tokens => tokens.GenerateMcpServiceToken(user, lifetimeDays: 1));
        await WithServiceTokenStoreAsync(store => store.AddAsync(new McpServiceToken(jti, user.Id, "test", expiresAt)));
        return (token, jti);
    }

    public static string MintToken(string userId, string? audience)
    {
        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, userId)]),
            Audience = audience,
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)), SecurityAlgorithms.HmacSha256),
        });
        return handler.WriteToken(token);
    }

    private void ReplaceAuthStoreWithInMemory(IServiceCollection services)
    {
        var providerRegistrations = services
            .Where(d => d.ServiceType == typeof(DbContextOptions<AuthDbContext>)
                     || d.ServiceType == typeof(AuthDbContext)
                     || d.ServiceType == typeof(IDbContextOptionsConfiguration<AuthDbContext>))
            .ToList();
        foreach (var registration in providerRegistrations)
            services.Remove(registration);

        services.AddDbContext<AuthDbContext>(options => options.UseInMemoryDatabase("mcp-auth-pipeline", _authDbRoot));
    }
}
