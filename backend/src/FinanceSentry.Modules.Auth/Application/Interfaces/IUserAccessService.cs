using FinanceSentry.Modules.Auth.Domain.Entities;

namespace FinanceSentry.Modules.Auth.Application.Interfaces;

/// <summary>A user's roles and effective permissions, and the role a new account starts with.</summary>
public interface IUserAccessService
{
    /// <summary>
    /// Roles from Identity's user-role table; permissions are the distinct <c>permission</c> claims on the
    /// principal Identity's claims factory builds (role grants plus per-person grants), the same set the
    /// authorization policies check.
    /// </summary>
    Task<UserAccess> GetAsync(ApplicationUser user);

    /// <summary>
    /// Gives a newly created account the Member role. Skipped when the role does not exist yet (a failed
    /// startup seed); the next startup seed makes every role-less account a Member.
    /// </summary>
    Task GrantDefaultRoleAsync(ApplicationUser user);
}

public sealed record UserAccess(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);
