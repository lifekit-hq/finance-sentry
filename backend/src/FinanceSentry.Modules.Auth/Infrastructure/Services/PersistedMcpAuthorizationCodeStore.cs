using System.Security.Cryptography;
using System.Text;
using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceSentry.Modules.Auth.Infrastructure.Services;

// McpAuthorizationCode rows run under AuthDbContext's Owner query filter. The code is redeemed at the anonymous MCP
// token endpoint, where no person is in scope, so the redeem opts out and matches on the code hash alone.
public sealed class PersistedMcpAuthorizationCodeStore(AuthDbContext db) : IMcpAuthorizationCodeStore
{
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);

    public async Task<string> IssueAsync(string userId, string email, string redirectUri, CancellationToken cancellationToken = default)
    {
        var rawCode = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        db.McpAuthorizationCodes.Add(new McpAuthorizationCode(
            userId,
            email,
            Hash(rawCode),
            redirectUri,
            DateTime.UtcNow.Add(CodeLifetime)));

        await db.SaveChangesAsync(cancellationToken);
        return rawCode;
    }

    public async Task<McpAuthorizationCodePayload?> ConsumeUnscopedAsync(string code, string redirectUri, CancellationToken cancellationToken = default)
    {
        var codeHash = Hash(code);
        var entity = await db.McpAuthorizationCodes.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .FirstOrDefaultAsync(x => x.CodeHash == codeHash, cancellationToken);

        if (entity is null || !entity.IsValidFor(redirectUri))
            return null;

        entity.Consume();
        await db.SaveChangesAsync(cancellationToken);

        return new McpAuthorizationCodePayload(entity.UserId, entity.Email, entity.RedirectUri);
    }

    private static string Hash(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
