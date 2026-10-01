namespace FinanceSentry.Tests.Integration.Auth;

using System.Security.Claims;
using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.Domain.Entities;
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
/// The startup role seed (<see cref="RoleSeeder"/>): each role exists with exactly the permissions
/// <see cref="Permissions.ByRole"/> gives it; the <see cref="AuthRoles.Owner"/> role goes to the account named
/// by <c>Auth:OwnerEmail</c>, else the sole user while nobody holds it, never more; every other account
/// without a role becomes a <see cref="AuthRoles.Member"/>; per-person grants are left alone.
/// </summary>
public sealed class RoleSeederTests : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    public RoleSeederTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var databaseName = $"role-seed-{Guid.NewGuid()}";
        services.AddDbContext<AuthDbContext>(o => o.UseInMemoryDatabase(databaseName));
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AuthDbContext>();
        _services = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Seed_WithSingleUserAndNoConfiguredOwner_GrantsThatUser()
    {
        await CreateUserAsync("only@test.com");

        await SeedAsync(ownerEmail: null);

        (await OwnersAsync()).Should().Equal("only@test.com");
    }

    [Fact]
    public async Task Seed_WithSeveralUsersAndNoConfiguredOwner_GrantsNobody()
    {
        await CreateUserAsync("first@test.com");
        await CreateUserAsync("second@test.com");

        await SeedAsync(ownerEmail: null);

        (await OwnersAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Seed_WithConfiguredOwner_GrantsOnlyThatUser()
    {
        await CreateUserAsync("first@test.com");
        await CreateUserAsync("second@test.com");

        await SeedAsync(ownerEmail: "second@test.com");

        (await OwnersAsync()).Should().Equal("second@test.com");
    }

    [Fact]
    public async Task Seed_RunAgainAfterGrant_IsANoOp()
    {
        await CreateUserAsync("only@test.com");
        await SeedAsync(ownerEmail: null);

        await SeedAsync(ownerEmail: null);

        (await OwnersAsync()).Should().Equal("only@test.com");
    }

    [Fact]
    public async Task Seed_WithConfiguredOwnerNotRegistered_CreatesRoleButGrantsNobody()
    {
        await CreateUserAsync("only@test.com");

        await SeedAsync(ownerEmail: "missing@test.com");

        using var scope = _services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>().RoleExistsAsync(AuthRoles.Owner))
            .Should().BeTrue();
        (await OwnersAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Seed_GivesEachRoleExactlyItsPermissions()
    {
        await SeedAsync(ownerEmail: null);

        (await RolePermissionsAsync(AuthRoles.Owner)).Should().BeEquivalentTo(Permissions.All);
        (await RolePermissionsAsync(AuthRoles.Member)).Should().Equal(Permissions.ConnectionsManage);
    }

    [Fact]
    public async Task Seed_RemovesAPermissionTheCodeNoLongerGrantsARole()
    {
        await SeedAsync(ownerEmail: null);
        using (var scope = _services.CreateScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var member = await roles.FindByNameAsync(AuthRoles.Member);
            await roles.AddClaimAsync(member!, new Claim(Permissions.ClaimType, Permissions.OpsAdmin));
        }

        await SeedAsync(ownerEmail: null);

        (await RolePermissionsAsync(AuthRoles.Member)).Should().Equal(Permissions.ConnectionsManage);
    }

    [Fact]
    public async Task Seed_MakesEveryoneButTheOwnerAMember_AndIsIdempotent()
    {
        await CreateUserAsync("owner@test.com");
        await CreateUserAsync("second@test.com");
        await CreateUserAsync("third@test.com");

        await SeedAsync(ownerEmail: "owner@test.com");
        await SeedAsync(ownerEmail: "owner@test.com");

        (await OwnersAsync()).Should().Equal("owner@test.com");
        (await MembersAsync()).Should().BeEquivalentTo("second@test.com", "third@test.com");
    }

    [Fact]
    public async Task Seed_LeavesPerPersonGrantsAlone()
    {
        await CreateUserAsync("first@test.com");
        await CreateUserAsync("second@test.com");
        using (var scope = _services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync("second@test.com");
            await users.AddClaimAsync(user!, new Claim(Permissions.ClaimType, Permissions.McpConnect));
        }

        await SeedAsync(ownerEmail: null);

        using var check = _services.CreateScope();
        var userManager = check.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var claims = await userManager.GetClaimsAsync((await userManager.FindByEmailAsync("second@test.com"))!);
        claims.Should().ContainSingle(c => c.Type == Permissions.ClaimType && c.Value == Permissions.McpConnect);
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    private async Task SeedAsync(string? ownerEmail)
    {
        using var scope = _services.CreateScope();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [RoleSeeder.OwnerEmailConfigKey] = ownerEmail })
            .Build();
        await RoleSeeder.SeedAsync(
            scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>(),
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
            config,
            NullLogger.Instance);
    }

    private async Task CreateUserAsync(string email)
    {
        using var scope = _services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await users.CreateAsync(new ApplicationUser { UserName = email, Email = email })).Succeeded.Should().BeTrue();
    }

    private Task<List<string?>> OwnersAsync() => UsersInRoleAsync(AuthRoles.Owner);

    private Task<List<string?>> MembersAsync() => UsersInRoleAsync(AuthRoles.Member);

    private async Task<List<string?>> UsersInRoleAsync(string role)
    {
        using var scope = _services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (await users.GetUsersInRoleAsync(role)).Select(u => u.Email).ToList();
    }

    private async Task<List<string>> RolePermissionsAsync(string roleName)
    {
        using var scope = _services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var role = await roles.FindByNameAsync(roleName);
        return (await roles.GetClaimsAsync(role!))
            .Where(c => c.Type == Permissions.ClaimType)
            .Select(c => c.Value)
            .ToList();
    }
}
