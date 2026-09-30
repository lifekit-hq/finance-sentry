namespace FinanceSentry.Tests.Integration.Shared;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Seeds the local account a test access token names. The API builds the request principal from that
/// account, so a token whose <c>sub</c> has no account is rejected with 401 and its roles are the account's.
/// </summary>
public static class TestUsers
{
    private static readonly Lock SeedLock = new();

    public static void EnsureExists(IServiceProvider services, Guid userId, bool owner = false)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var id = userId.ToString();
        var email = $"{id}@test.local";

        lock (SeedLock)
        {
            if (!db.Users.Any(u => u.Id == id))
                AddUser(db, id, email);

            if (owner)
                GrantOwner(scope.ServiceProvider, id);
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

    private static void GrantOwner(IServiceProvider services, string userId)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        if (!roles.RoleExistsAsync(AuthRoles.Owner).GetAwaiter().GetResult())
            roles.CreateAsync(new IdentityRole(AuthRoles.Owner)).GetAwaiter().GetResult();

        var user = users.FindByIdAsync(userId).GetAwaiter().GetResult()!;
        if (!users.IsInRoleAsync(user, AuthRoles.Owner).GetAwaiter().GetResult())
            users.AddToRoleAsync(user, AuthRoles.Owner).GetAwaiter().GetResult();
    }
}
