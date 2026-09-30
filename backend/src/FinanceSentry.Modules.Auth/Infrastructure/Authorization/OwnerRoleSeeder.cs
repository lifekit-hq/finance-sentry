namespace FinanceSentry.Modules.Auth.Infrastructure.Authorization;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// Startup seed for the <see cref="AuthRoles.Owner"/> role. Creates the role if missing, then grants it to
/// the account named by <c>Auth:OwnerEmail</c>. With that setting empty, the role goes to the only user
/// when exactly one exists and nobody holds it yet — a single-user deployment needs no configuration.
/// Only ever adds the role; removing it is a deliberate manual step.
/// </summary>
public static class OwnerRoleSeeder
{
    public const string OwnerEmailConfigKey = "Auth:OwnerEmail";

    public static async Task SeedAsync(
        RoleManager<IdentityRole> roles,
        UserManager<ApplicationUser> users,
        IConfiguration config,
        ILogger logger)
    {
        if (!await roles.RoleExistsAsync(AuthRoles.Owner))
            EnsureSucceeded(await roles.CreateAsync(new IdentityRole(AuthRoles.Owner)), "create the Owner role");

        var owner = await ResolveOwnerAsync(users, config[OwnerEmailConfigKey], logger);
        if (owner is null || await users.IsInRoleAsync(owner, AuthRoles.Owner))
            return;

        EnsureSucceeded(await users.AddToRoleAsync(owner, AuthRoles.Owner), "grant the Owner role");
        logger.LogInformation("Granted the {Role} role to user {UserId}.", AuthRoles.Owner, owner.Id);
    }

    private static async Task<ApplicationUser?> ResolveOwnerAsync(
        UserManager<ApplicationUser> users, string? ownerEmail, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(ownerEmail))
        {
            var configured = await users.FindByEmailAsync(ownerEmail.Trim());
            if (configured is null)
                logger.LogWarning("{ConfigKey} names no existing account; the {Role} role was not granted.",
                    OwnerEmailConfigKey, AuthRoles.Owner);
            return configured;
        }

        if ((await users.GetUsersInRoleAsync(AuthRoles.Owner)).Count > 0)
            return null;

        var candidates = await users.Users.OrderBy(u => u.Id).Take(2).ToListAsync();
        if (candidates.Count == 1)
            return candidates[0];

        if (candidates.Count > 1)
            logger.LogWarning("No user holds the {Role} role and {ConfigKey} is not set; set it to name the owner account.",
                AuthRoles.Owner, OwnerEmailConfigKey);
        return null;
    }

    private static void EnsureSucceeded(IdentityResult result, string action)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Failed to {action}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
    }
}
