namespace FinanceSentry.Tests.Integration.Auth;

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
/// Which account <see cref="OwnerRoleSeeder"/> grants the <see cref="AuthRoles.Owner"/> role at startup:
/// the one named by <c>Auth:OwnerEmail</c>, else the sole user while nobody holds the role; never more.
/// </summary>
public sealed class OwnerRoleSeederTests : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    public OwnerRoleSeederTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var databaseName = $"owner-seed-{Guid.NewGuid()}";
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

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    private async Task SeedAsync(string? ownerEmail)
    {
        using var scope = _services.CreateScope();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [OwnerRoleSeeder.OwnerEmailConfigKey] = ownerEmail })
            .Build();
        await OwnerRoleSeeder.SeedAsync(
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

    private async Task<List<string?>> OwnersAsync()
    {
        using var scope = _services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (await users.GetUsersInRoleAsync(AuthRoles.Owner)).Select(u => u.Email).ToList();
    }
}
