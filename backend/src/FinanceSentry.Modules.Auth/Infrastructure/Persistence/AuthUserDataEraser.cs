using FinanceSentry.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FinanceSentry.Modules.Auth.Infrastructure.Persistence;

/// <summary>
/// Erases the person's credential rows: refresh tokens, pending MCP authorization codes and MCP service tokens. The
/// Identity account itself (with its claims, logins and roles, which cascade from it) is deleted by the closure purge
/// through <c>UserManager</c>, after every module's eraser has run.
/// </summary>
internal sealed class AuthUserDataEraser(AuthDbContext db) : IUserDataEraser
{
    public string Module => "Auth";

    public async Task<int> EraseAsync(Guid userId, CancellationToken ct = default)
    {
        var key = userId.ToString();

        var removed = await db.RefreshTokens.IgnoreQueryFilters().Where(t => t.UserId == key).ExecuteDeleteAsync(ct);
        removed += await db.McpAuthorizationCodes.IgnoreQueryFilters().Where(c => c.UserId == key).ExecuteDeleteAsync(ct);
        removed += await db.McpServiceTokens.Where(t => t.UserId == key).ExecuteDeleteAsync(ct);

        return removed;
    }
}
