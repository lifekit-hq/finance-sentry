using FinanceSentry.Modules.Auth.Domain.Entities;

namespace FinanceSentry.Modules.Auth.Application.Interfaces;

/// <summary>
/// Creates a passwordless Member account for an email: the one path both a People-page invite and a first
/// sign-in that carries the app's identity-provider role take.
/// </summary>
public interface IMemberAccountCreator
{
    /// <summary>
    /// Creates the account, gives it the Member role and announces it to the other modules. Throws a
    /// <c>ValidationException</c> on the email when Identity rejects the account.
    /// </summary>
    Task<ApplicationUser> CreateAsync(string email, CancellationToken cancellationToken);
}
