using System.Security.Cryptography;
using System.Text;
using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceSentry.Modules.Auth.Infrastructure.Services;

// RefreshToken rows run under AuthDbContext's Owner query filter, but every read here opts out: a raw token is the
// credential itself and is presented to anonymous endpoints (refresh, session probe, logout, MCP token exchange)
// whose access token has expired or was never sent, and a bulk revoke names its user explicitly. Each opt-out keeps
// its own predicate (the token hash, or the named user id).
public class RefreshTokenService(AuthDbContext db) : IRefreshTokenService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(30);

    private IQueryable<RefreshToken> AllUsers => db.RefreshTokens.IgnoreQueryFilters([OwnerQueryFilter.Name]);

    public async Task<(string RawToken, RefreshToken Entity)> IssueAsync(
          string userId, CancellationToken cancellationToken = default)
    {
        var raw = GenerateRawToken();
        var hash = Hash(raw);
        var entity = new RefreshToken(userId, hash, DateTime.UtcNow.Add(TokenLifetime));

        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return (raw, entity);
    }

    public async Task<RefreshToken?> ValidateUnscopedAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        var hash = Hash(rawToken);
        var entity = await AllUsers
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        return entity?.IsValid() == true ? entity : null;
    }

    public async Task<(string RawToken, RefreshToken Entity)> RotateAsync(
        RefreshToken existing, CancellationToken cancellationToken = default)
    {
        existing.Revoke();

        var raw = GenerateRawToken();
        var hash = Hash(raw);
        var next = new RefreshToken(existing.UserId, hash, DateTime.UtcNow.Add(TokenLifetime));

        db.RefreshTokens.Add(next);
        await db.SaveChangesAsync(cancellationToken);

        return (raw, next);
    }

    public async Task RevokeAsync(string userId, CancellationToken cancellationToken = default)
    {
        var tokens = await AllUsers
            .Where(t => t.UserId == userId && !t.IsRevoked)
            .ToListAsync(cancellationToken);

        foreach (var t in tokens)
            t.Revoke();

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeTokenUnscopedAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        var hash = Hash(rawToken);
        var entity = await AllUsers
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (entity is null || entity.IsRevoked)
            return;

        entity.Revoke();
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string GenerateRawToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    private static string Hash(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
