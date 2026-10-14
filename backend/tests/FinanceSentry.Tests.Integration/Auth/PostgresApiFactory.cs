namespace FinanceSentry.Tests.Integration.Auth;

using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

/// <summary>
/// The whole API over a real, freshly migrated Postgres database. TrueLayer is a loose mock, so a closure's
/// best-effort revoke never leaves the test process.
/// </summary>
internal sealed class PostgresApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("ConnectionStrings:ReadOnly", connectionString);
        builder.UseSetting("Deduplication:MasterKeyBase64", "dGVzdC1vbmx5LWtleS1ub3QtdGhlLWxlYWtlZC1vbmU=");
        builder.UseSetting("Encryption:CurrentKeyVersion", "1");
        builder.UseSetting("Encryption:Keys:1", "dGVzdC1vbmx5LWtleS1ub3QtdGhlLWxlYWtlZC1vbmU=");
        builder.UseSetting("Jwt:Secret", "test-jwt-secret-key-for-integration-tests-minimum-32-chars");
        builder.UseSetting("Jwt:ExpiryMinutes", "60");
        builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Scoped(_ => Mock.Of<ITrueLayerClient>())));
    }

    /// <summary>An account as Identity creates it, in <paramref name="role"/> when one is given.</summary>
    public async Task CreateAccountAsync(Guid userId, string? role = null)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{userId}@test.local";
        var created = await users.CreateAsync(new ApplicationUser { Id = userId.ToString(), UserName = email, Email = email });
        created.Succeeded.Should().BeTrue();
        if (role is not null)
            (await users.AddToRoleAsync((await users.FindByIdAsync(userId.ToString()))!, role)).Succeeded.Should().BeTrue();
    }

    /// <summary>A client signed in as the account, with an access token minted for its current roles.</summary>
    public async Task<HttpClient> ClientForAsync(Guid userId)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(userId.ToString()))!;
        var (token, _) = scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateToken(user, await users.GetRolesAsync(user));

        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", $"fs_access_token={token}");
        return client;
    }
}
