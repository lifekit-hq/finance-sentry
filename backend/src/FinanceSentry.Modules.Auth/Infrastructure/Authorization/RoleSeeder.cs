namespace FinanceSentry.Modules.Auth.Infrastructure.Authorization;

using System.Security.Claims;
using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// Idempotent startup seed for roles and their permissions:
/// <list type="number">
/// <item>Creates each role in <see cref="AuthRoles.All"/> and converges its permission claims to
/// <see cref="Permissions.ByRole"/> (adds missing, removes ones the code no longer grants).</item>
/// <item>Grants <see cref="AuthRoles.Owner"/> to the account named by <c>Auth:OwnerEmail</c>. With that
/// setting empty, the role goes to the only user when exactly one exists and nobody holds it yet, so a
/// single-user deployment needs no configuration. Never removes the Owner role.</item>
/// <item>Gives every user who holds no role the <see cref="AuthRoles.Member"/> role.</item>
/// </list>
/// Per-person permission grants live in the user-claim table and are left alone.
/// </summary>
public static class RoleSeeder
{
    public const string OwnerEmailConfigKey = "Auth:OwnerEmail";

    public static async Task SeedAsync(
        RoleManager<IdentityRole> roles,
        UserManager<ApplicationUser> users,
        IConfiguration config,
        ILogger logger)
    {
        foreach (var roleName in AuthRoles.All)
            await ConvergeRoleAsync(roles, roleName, Permissions.ByRole[roleName], logger);

        await SeedOwnerAsync(users, config[OwnerEmailConfigKey], logger);
        await SeedMembersAsync(users, logger);
    }

    private static async Task ConvergeRoleAsync(
        RoleManager<IdentityRole> roles, string roleName, IReadOnlyList<string> permissions, ILogger logger)
    {
        var role = await roles.FindByNameAsync(roleName);
        if (role is null)
        {
            role = new IdentityRole(roleName);
            EnsureSucceeded(await roles.CreateAsync(role), $"create the {roleName} role");
        }

        var held = (await roles.GetClaimsAsync(role)).Where(c => c.Type == Permissions.ClaimType).ToList();

        foreach (var permission in permissions.Where(p => held.All(c => c.Value != p)))
        {
            EnsureSucceeded(await roles.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission)),
                $"grant {permission} to the {roleName} role");
            logger.LogInformation("Granted {Permission} to the {Role} role.", permission, roleName);
        }

        foreach (var stale in held.Where(c => !permissions.Contains(c.Value)))
        {
            EnsureSucceeded(await roles.RemoveClaimAsync(role, stale),
                $"remove {stale.Value} from the {roleName} role");
            logger.LogInformation("Removed {Permission} from the {Role} role.", stale.Value, roleName);
        }
    }

    private static async Task SeedOwnerAsync(UserManager<ApplicationUser> users, string? ownerEmail, ILogger logger)
    {
        var owner = await ResolveOwnerAsync(users, ownerEmail, logger);
        if (owner is null || await users.IsInRoleAsync(owner, AuthRoles.Owner))
            return;

        EnsureSucceeded(await users.AddToRoleAsync(owner, AuthRoles.Owner), "grant the Owner role");
        logger.LogInformation("Granted the {Role} role to user {UserId}.", AuthRoles.Owner, owner.Id);
    }

    private static async Task SeedMembersAsync(UserManager<ApplicationUser> users, ILogger logger)
    {
        foreach (var user in await users.Users.OrderBy(u => u.Id).ToListAsync())
        {
            if ((await users.GetRolesAsync(user)).Count > 0)
                continue;

            EnsureSucceeded(await users.AddToRoleAsync(user, AuthRoles.Member), "grant the Member role");
            logger.LogInformation("Granted the {Role} role to user {UserId}.", AuthRoles.Member, user.Id);
        }
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
