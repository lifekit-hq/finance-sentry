namespace FinanceSentry.Tests.Integration.Shared;

using System.Security.Claims;
using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Seeds the local account a test access token names. The API builds the request principal from that
/// account, so a token whose <c>sub</c> has no account is rejected with 401, and its roles and permissions
/// are the account's. Accounts are Members unless another role is asked for, as registration makes them.
/// </summary>
public static class TestUsers
{
    private static readonly Lock SeedLock = new();

    public static void EnsureExists(IServiceProvider services, Guid userId, string role = AuthRoles.Member)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var id = userId.ToString();
        var email = $"{id}@test.local";

        lock (SeedLock)
        {
            if (!db.Users.Any(u => u.Id == id))
                AddUser(db, id, email);

            GrantRole(scope.ServiceProvider, id, role);
        }
    }

    /// <summary>
    /// Puts the account in <paramref name="role"/>, creating the role with the permissions
    /// <see cref="Permissions.ByRole"/> gives it when a test host has not seeded it.
    /// </summary>
    public static void GrantRole(IServiceProvider services, string userId, string role)
    {
        lock (SeedLock)
        {
            var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            EnsureRole(roles, role);

            var user = users.FindByIdAsync(userId).GetAwaiter().GetResult()!;
            if (!users.IsInRoleAsync(user, role).GetAwaiter().GetResult())
                users.AddToRoleAsync(user, role).GetAwaiter().GetResult();
        }
    }

    private static void EnsureRole(RoleManager<IdentityRole> roles, string roleName)
    {
        var role = roles.FindByNameAsync(roleName).GetAwaiter().GetResult();
        if (role is null)
        {
            role = new IdentityRole(roleName);
            roles.CreateAsync(role).GetAwaiter().GetResult();
        }

        var held = roles.GetClaimsAsync(role).GetAwaiter().GetResult();
        foreach (var permission in Permissions.ByRole[roleName])
        {
            if (!held.Any(c => c.Type == Permissions.ClaimType && c.Value == permission))
                roles.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission)).GetAwaiter().GetResult();
        }
    }

    private static void AddUser(AuthDbContext db, string id, string email)
    {
        db.Users.Add(new ApplicationUser
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
        });
        db.SaveChanges();
    }
}
