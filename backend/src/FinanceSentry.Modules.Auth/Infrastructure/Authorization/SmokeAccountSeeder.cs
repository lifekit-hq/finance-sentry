namespace FinanceSentry.Modules.Auth.Infrastructure.Authorization;

using System.Security.Claims;
using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.People;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// Idempotent startup seed for the account the post-deploy live smoke signs in with. With both
/// <c>Auth:SmokeAccount:Email</c> and <c>Auth:SmokeAccount:Password</c> set:
/// <list type="number">
/// <item>Creates the account, marked with <see cref="MarkerClaim"/>, as a <see cref="AuthRoles.Member"/>.</item>
/// <item>On later runs converges a marked account's password to the configured one, so rotating the secret
/// needs only a redeploy.</item>
/// </list>
/// An existing account without the marker is never touched, so the setting cannot take over a real person's
/// account, and a revoked smoke account stays revoked. <see cref="RoleSeeder"/> never makes a marked account
/// the owner. With either setting empty the seed does nothing.
/// </summary>
public static class SmokeAccountSeeder
{
    public const string EmailConfigKey = "Auth:SmokeAccount:Email";
    public const string PasswordConfigKey = "Auth:SmokeAccount:Password";

    /// <summary>User claim marking the one account this seed created and may manage.</summary>
    public static readonly Claim MarkerClaim = new("seeded-account", "smoke");

    /// <summary>
    /// Ensures the smoke account and returns its user id, or null when the seed is not configured or the
    /// configured email belongs to an account the seed may not manage.
    /// </summary>
    public static async Task<Guid?> SeedAsync(UserManager<ApplicationUser> users, IConfiguration config, ILogger logger)
    {
        var email = config[EmailConfigKey]?.Trim();
        var password = config[PasswordConfigKey];
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            if (!string.IsNullOrEmpty(email) || !string.IsNullOrEmpty(password))
                logger.LogWarning("Only one of {EmailKey} and {PasswordKey} is set; the smoke account was not seeded.",
                    EmailConfigKey, PasswordConfigKey);
            return null;
        }

        var user = await users.FindByEmailAsync(email);
        if (user is null)
            return await CreateAsync(users, email, password, logger);

        if (!await IsMarkedAsync(users, user))
        {
            logger.LogWarning("{ConfigKey} names an account the smoke seed did not create; it was left alone.",
                EmailConfigKey);
            return null;
        }

        if (PersonStatus.IsRevoked(user))
        {
            logger.LogInformation("The smoke account {UserId} is revoked; the seed left it alone.", user.Id);
            return null;
        }

        if (!await users.CheckPasswordAsync(user, password))
        {
            if (await users.HasPasswordAsync(user))
                EnsureSucceeded(await users.RemovePasswordAsync(user), "clear the smoke account password");
            EnsureSucceeded(await users.AddPasswordAsync(user, password), "set the smoke account password");
            logger.LogInformation("Updated the smoke account {UserId} password from configuration.", user.Id);
        }

        return Guid.Parse(user.Id);
    }

    public static async Task<bool> IsMarkedAsync(UserManager<ApplicationUser> users, ApplicationUser user) =>
        (await users.GetClaimsAsync(user)).Any(c => c.Type == MarkerClaim.Type && c.Value == MarkerClaim.Value);

    private static async Task<Guid> CreateAsync(
        UserManager<ApplicationUser> users, string email, string password, ILogger logger)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = "Smoke",
            LastName = "Test",
        };
        EnsureSucceeded(await users.CreateAsync(user, password), "create the smoke account");
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
