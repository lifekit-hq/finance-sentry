using FinanceSentry.Core.Auth;

namespace FinanceSentry.Mcp.Abstractions;

/// <summary>
/// <see cref="ICurrentUser"/> for the MCP host: the same identity the tools act for - the HTTP transport's
/// authenticated user, or the stdio transport's local session.
/// </summary>
public sealed class IdentityResolverCurrentUser(IIdentityResolver identityResolver) : ICurrentUser
{
    public Guid? UserId => identityResolver.GetUserId();
}
