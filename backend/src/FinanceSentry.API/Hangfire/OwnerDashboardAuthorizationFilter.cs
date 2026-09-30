using FinanceSentry.Core.Auth;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authorization;

namespace FinanceSentry.API.Hangfire;

/// <summary>
/// Hangfire dashboard authorization outside Development (FR-004): only the signed-in owner gets in.
/// The caller's identity comes from the <c>fs_access_token</c> cookie (attached to <c>HttpContext.User</c>
/// by <c>JwtAuthenticationMiddleware</c>) and must satisfy <see cref="AuthPolicies.RequireOwner"/>.
/// Every other caller is denied outright — no read-only view — whatever network it arrives from:
/// Hangfire answers 401 without an identity and 403 for a signed-in non-owner.
/// </summary>
public sealed class OwnerDashboardAuthorizationFilter : IDashboardAsyncAuthorizationFilter
{
    public async Task<bool> AuthorizeAsync(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        if (httpContext.User.Identity?.IsAuthenticated != true)
            return false;

        var authorization = httpContext.RequestServices.GetRequiredService<IAuthorizationService>();
        var result = await authorization.AuthorizeAsync(httpContext.User, AuthPolicies.RequireOwner);
        return result.Succeeded;
    }
}
