namespace FinanceSentry.Modules.Auth.Infrastructure.Authentication;

using System.Security.Claims;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;

/// <summary>
/// Builds the request principal for a validated access token from the local account its <c>sub</c> names,
/// with Identity's <see cref="IUserClaimsPrincipalFactory{TUser}"/> (id, name, email, roles, security stamp).
/// Returns <c>null</c> when the account is missing or locked out, so the token is rejected. A built
/// principal is cached for <see cref="CacheLifetime"/>, which bounds how long a role or lockout change
/// takes to reach a live session.
/// </summary>
public sealed class AccessTokenPrincipalLoader(
    UserManager<ApplicationUser> users,
    IUserClaimsPrincipalFactory<ApplicationUser> principalFactory,
    IMemoryCache cache) : IAccessTokenPrincipalLoader
{
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(60);

    public async Task<ClaimsPrincipal?> LoadAsync(string userId)
    {
        var cacheKey = $"auth:access-token-principal:{userId}";
        if (cache.TryGetValue(cacheKey, out ClaimsPrincipal? cached) && cached is not null)
            return cached.Clone();

        var user = await users.FindByIdAsync(userId);
        if (user is null || await users.IsLockedOutAsync(user))
            return null;

        var principal = await principalFactory.CreateAsync(user);
        cache.Set(cacheKey, principal, CacheLifetime);
        return principal.Clone();
    }
}
