namespace FinanceSentry.Tests.Integration.Auth;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.People;
using FinanceSentry.Modules.Auth.Infrastructure.Authorization;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// The startup smoke-account seed (<see cref="SmokeAccountSeeder"/>): with both settings it creates one marked
/// Member account with the configured password and keeps that password converged; without them it does nothing;
/// it never touches an account it did not create, and leaves a revoked smoke account revoked.
/// </summary>
public sealed class SmokeAccountSeederTests : IAsyncDisposable
{
    private const string Email = "smoke@test.com";
    private const string Password = "Smoke-Pass-1";
    private const string RotatedPassword = "Smoke-Pass-2";

    private readonly ServiceProvider _services;

    public SmokeAccountSeederTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var databaseName = $"smoke-seed-{Guid.NewGuid()}";
        services.AddSingleton<ICurrentUser>(NoCurrentUser.Instance);
        services.AddDbContext<AuthDbContext>(o => o.UseInMemoryDatabase(databaseName));
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AuthDbContext>();
        _services = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Seed_WithoutSettings_DoesNothing()
    {
        (await SeedAsync(email: null, password: null)).Should().BeNull();
        (await SeedAsync(email: Email, password: null)).Should().BeNull();
        (await SeedAsync(email: null, password: Password)).Should().BeNull();

        (await FindAsync(Email)).Should().BeNull();
    }

    [Fact]
    public async Task Seed_CreatesAMarkedMemberWithTheConfiguredPassword()
    {
        var userId = await SeedAsync(Email, Password);

        using var scope = _services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(Email);
        user.Should().NotBeNull();
        userId.Should().Be(Guid.Parse(user!.Id));
        (await users.GetRolesAsync(user)).Should().Equal(AuthRoles.Member);
        (await users.CheckPasswordAsync(user, Password)).Should().BeTrue();
        (await SmokeAccountSeeder.IsMarkedAsync(users, user)).Should().BeTrue();
    }

    [Fact]
    public async Task Seed_RunAgain_ReturnsTheSameAccount()
    {
        var first = await SeedAsync(Email, Password);

        var second = await SeedAsync(Email, Password);

        second.Should().Be(first);
        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().Users.Should().ContainSingle();
    }

    [Fact]
    public async Task Seed_WithARotatedPassword_UpdatesIt()
    {
        await SeedAsync(Email, Password);

        await SeedAsync(Email, RotatedPassword);

        using var scope = _services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(Email);
        (await users.CheckPasswordAsync(user!, RotatedPassword)).Should().BeTrue();
        (await users.CheckPasswordAsync(user!, Password)).Should().BeFalse();
    }

    [Fact]
    public async Task Seed_NamingAnAccountItDidNotCreate_LeavesItAlone()
    {
        using (var scope = _services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            (await users.CreateAsync(new ApplicationUser { UserName = Email, Email = Email }, "Real-Pass-1"))
                .Succeeded.Should().BeTrue();
        }

        (await SeedAsync(Email, Password)).Should().BeNull();

        using var check = _services.CreateScope();
        var userManager = check.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(Email);
        (await userManager.CheckPasswordAsync(user!, "Real-Pass-1")).Should().BeTrue();
        (await userManager.GetClaimsAsync(user!)).Should().BeEmpty();
    }

    [Fact]
    public async Task Seed_ARevokedSmokeAccount_StaysRevoked()
    {
        await SeedAsync(Email, Password);
        using (var scope = _services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync(Email);
            await users.SetLockoutEnabledAsync(user!, true);
            await users.SetLockoutEndDateAsync(user!, PersonStatus.RevokedLockoutEnd);
        }

        (await SeedAsync(Email, RotatedPassword)).Should().BeNull();

        using var check = _services.CreateScope();
        var userManager = check.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await userManager.CheckPasswordAsync((await userManager.FindByEmailAsync(Email))!, RotatedPassword))
            .Should().BeFalse();
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    // Runs the role seed first, as startup does, so the Member role exists.
    private async Task<Guid?> SeedAsync(string? email, string? password)
    {
        using var scope = _services.CreateScope();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SmokeAccountSeeder.EmailConfigKey] = email,
                [SmokeAccountSeeder.PasswordConfigKey] = password,
            })
            .Build();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await RoleSeeder.SeedAsync(
            scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>(), users, config, NullLogger.Instance);
        return await SmokeAccountSeeder.SeedAsync(users, config, NullLogger.Instance);
    }

    private async Task<ApplicationUser?> FindAsync(string email)
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
    }
}
