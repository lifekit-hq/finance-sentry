namespace FinanceSentry.Modules.Auth.Domain.People;

using FinanceSentry.Modules.Auth.Domain.Entities;

/// <summary>
/// Where an account stands in invite-only onboarding, derived from Identity state rather than stored:
/// <list type="bullet">
/// <item><see cref="Invited"/>: created from the People page, no password set and no external login linked yet.</item>
/// <item><see cref="Active"/>: can sign in.</item>
/// <item><see cref="Revoked"/>: locked out until <see cref="RevokedLockoutEnd"/>, roles and grants removed.</item>
/// </list>
/// </summary>
public static class PersonStatus
{
    public const string Invited = "Invited";
    public const string Active = "Active";
    public const string Revoked = "Revoked";

    /// <summary>
    /// The lockout end a revoke sets. Far enough out to be permanent, and distinct from the short lockout that
    /// failed password attempts set, so a revoked account is told apart from a temporarily locked one.
    /// </summary>
    public static readonly DateTimeOffset RevokedLockoutEnd = new(9999, 12, 31, 0, 0, 0, TimeSpan.Zero);

    public static bool IsRevoked(ApplicationUser user) => user.LockoutEnd >= RevokedLockoutEnd;

    public static string Of(ApplicationUser user, bool hasExternalLogin) =>
        IsRevoked(user) ? Revoked
        : user.PasswordHash is null && !hasExternalLogin ? Invited
        : Active;
}
