namespace FinanceSentry.Modules.Auth.Infrastructure.Authentication;

using System.Security.Claims;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using Microsoft.AspNetCore.Identity;

/// <summary>
/// Builds the request principal for a validated access token from the local account its <c>sub</c> names,
/// with Identity's <see cref="IUserClaimsPrincipalFactory{TUser}"/> (id, name, email, roles, role and user
/// permission claims, security stamp). Returns <c>null</c> when the account is missing or locked out, so the
/// token is rejected. Built on every request and never cached: a role, permission or lockout change applies
/// to the next request without a new sign-in.
/// </summary>
public sealed class AccessTokenPrincipalLoader(
    UserManager<ApplicationUser> users,
    IUserClaimsPrincipalFactory<ApplicationUser> principalFactory) : IAccessTokenPrincipalLoader
{
    public async Task<ClaimsPrincipal?> LoadAsync(string userId)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null || await users.IsLockedOutAsync(user))
            return null;

        return await principalFactory.CreateAsync(user);
    }
}
