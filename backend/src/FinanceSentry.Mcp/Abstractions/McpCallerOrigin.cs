using FinanceSentry.Mcp.Authentication;
using FinanceSentry.Modules.Research.Domain;
using Microsoft.AspNetCore.Http;

namespace FinanceSentry.Mcp.Abstractions;

/// <summary>
/// Who a Research write is attributed to: a call on a service token is Ledger's, every other call (a personal token,
/// or stdio, which has no HTTP request and is always the owner's own session) is the owner's.
/// </summary>
public static class McpCallerOrigin
{
    public static ThesisEventOrigin Of(IHttpContextAccessor httpContextAccessor) =>
        McpAuthenticationExtensions.IsServiceToken(httpContextAccessor.HttpContext?.User)
            ? ThesisEventOrigin.Ledger
            : ThesisEventOrigin.User;
}
