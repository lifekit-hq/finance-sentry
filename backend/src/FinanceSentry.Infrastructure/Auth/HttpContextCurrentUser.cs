namespace FinanceSentry.Infrastructure.Auth;

using FinanceSentry.Core.Auth;
using Microsoft.AspNetCore.Http;

/// <summary>
/// <see cref="ICurrentUser"/> for an HTTP host: the authenticated request principal, or no user outside a
/// request (Hangfire jobs, startup work) or for an anonymous caller.
/// </summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            var principal = httpContextAccessor.HttpContext?.User;
            return principal?.Identity?.IsAuthenticated == true ? principal.GetUserId() : null;
        }
    }
}
