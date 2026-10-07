namespace FinanceSentry.Modules.Auth.Infrastructure.Authorization;

using System.Security.Claims;
using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.People;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// Idempotent startup seed for the account the post-deploy live smoke signs in as. The smoke reaches it through
/// the identity provider (a dedicated provider user with the same verified email), never with a password here.
/// With <c>Auth:SmokeAccount:Email</c> set:
/// <list type="number">
/// <item>Creates the account with no password, marked with <see cref="MarkerClaim"/>, as a <see cref="AuthRoles.Member"/>;
/// the first provider sign-in links it by verified email (<c>ExternalLoginCommand</c>).</item>
/// <item>On later runs removes any password a marked account still carries, so it is not a password sign-in target.</item>
/// </list>
/// An existing account without the marker is never touched, so the setting cannot take over a real person's
/// account, and a revoked smoke account stays revoked. <see cref="RoleSeeder"/> never makes a marked account
/// the owner. With the email empty the seed does nothing.
/// </summary>
public static class SmokeAccountSeeder
{
    public const string EmailConfigKey = "Auth:SmokeAccount:Email";

    /// <summary>User claim marking the one account this seed created and may manage.</summary>
    public static readonly Claim MarkerClaim = new("seeded-account", "smoke");

    /// <summary>
    /// Ensures the smoke account and returns its user id, or null when the seed is not configured or the
    /// configured email belongs to an account the seed may not manage.
    /// </summary>
    public static async Task<Guid?> SeedAsync(UserManager<ApplicationUser> users, IConfiguration config, ILogger logger)
    {
        var email = config[EmailConfigKey]?.Trim();
        if (string.IsNullOrEmpty(email))
            return null;

        var user = await users.FindByEmailAsync(email);
        if (user is null)
            return await CreateAsync(users, email, logger);

        if (!await IsMarkedAsync(users, user))
        {
            logger.LogWarning("The smoke account email setting names an account the smoke seed did not create; it was left alone.");
            return null;
        }

        if (PersonStatus.IsRevoked(user))
        {
            logger.LogInformation("The smoke account {UserId} is revoked; the seed left it alone.", user.Id);
            return null;
        }

        if (await users.HasPasswordAsync(user))
        {
            EnsureSucceeded(await users.RemovePasswordAsync(user), "remove the smoke account password");
            logger.LogInformation("Removed the password from the smoke account {UserId}; it signs in through the identity provider.", user.Id);
        }

        return Guid.Parse(user.Id);
    }

    public static async Task<bool> IsMarkedAsync(UserManager<ApplicationUser> users, ApplicationUser user) =>
        (await users.GetClaimsAsync(user)).Any(c => c.Type == MarkerClaim.Type && c.Value == MarkerClaim.Value);

    private static async Task<Guid> CreateAsync(
        UserManager<ApplicationUser> users, string email, ILogger logger)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = "Smoke",
            LastName = "Test",
        };
        EnsureSucceeded(await users.CreateAsync(user), "create the smoke account");
        EnsureSucceeded(await users.AddClaimAsync(user, MarkerClaim), "mark the smoke account");
        EnsureSucceeded(await users.AddToRoleAsync(user, AuthRoles.Member), "make the smoke account a Member");
        logger.LogInformation("Created the smoke account {UserId}.", user.Id);
        return Guid.Parse(user.Id);
    }

    private static void EnsureSucceeded(IdentityResult result, string action)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Failed to {action}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
    }
}
