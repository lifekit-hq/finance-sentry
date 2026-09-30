using System.Security.Claims;

namespace FinanceSentry.Modules.Auth.Application.Interfaces;

public interface IAccessTokenPrincipalLoader
{
    /// <summary>
    /// Builds the request principal for a validated access token from the local account <paramref name="userId"/>
    /// names; <c>null</c> when the account is missing or locked out, so the token is rejected.
    /// </summary>
    Task<ClaimsPrincipal?> LoadAsync(string userId);
}
