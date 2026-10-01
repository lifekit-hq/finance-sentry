namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Evaluates a named authorization policy for a user outside a request (background jobs acting for that
/// user). Builds the same principal a request would carry, so jobs and endpoints agree on who may do what.
/// </summary>
public interface IUserAuthorizationChecker
{
    /// <summary>False when the user is missing or the policy fails.</summary>
    Task<bool> IsAuthorizedAsync(Guid userId, string policy, CancellationToken ct = default);
}
