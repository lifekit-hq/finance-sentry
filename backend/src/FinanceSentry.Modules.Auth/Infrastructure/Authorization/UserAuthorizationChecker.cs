namespace FinanceSentry.Modules.Auth.Infrastructure.Authorization;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

/// <summary>
/// Builds the user's principal with Identity's claims-principal factory (as a request would) and evaluates
/// the named policy against it. A missing or locked-out account is not authorized.
/// </summary>
public sealed class UserAuthorizationChecker(
    UserManager<ApplicationUser> users,
    IUserClaimsPrincipalFactory<ApplicationUser> principalFactory,
    IAuthorizationService authorization) : IUserAuthorizationChecker
{
    public async Task<bool> IsAuthorizedAsync(Guid userId, string policy, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || await users.IsLockedOutAsync(user))
            return false;

        var principal = await principalFactory.CreateAsync(user);
        return (await authorization.AuthorizeAsync(principal, policy)).Succeeded;
    }
}
