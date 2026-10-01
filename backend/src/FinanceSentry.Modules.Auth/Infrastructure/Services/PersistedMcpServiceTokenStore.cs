using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceSentry.Modules.Auth.Infrastructure.Services;

public sealed class PersistedMcpServiceTokenStore(AuthDbContext db) : IMcpServiceTokenStore
{
    public async Task AddAsync(McpServiceToken token, CancellationToken cancellationToken = default)
    {
        db.McpServiceTokens.Add(token);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> IsActiveAsync(Guid jti, CancellationToken cancellationToken = default)
        => db.McpServiceTokens.AsNoTracking()
            .AnyAsync(t => t.Id == jti && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow, cancellationToken);

    public async Task<bool> RevokeAsync(Guid jti, CancellationToken cancellationToken = default)
    {
        var entity = await db.McpServiceTokens.FirstOrDefaultAsync(t => t.Id == jti, cancellationToken);
        if (entity is null || entity.RevokedAt is not null)
        {
            return false;
        }

        entity.Revoke();
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> RevokeAllAsync(string userId, CancellationToken cancellationToken = default)
    {
        var unrevoked = await db.McpServiceTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in unrevoked)
        {
            token.Revoke();
        }

        await db.SaveChangesAsync(cancellationToken);
        return unrevoked.Count;
    }

    public async Task<IReadOnlyList<McpServiceToken>> ListAsync(string userId, CancellationToken cancellationToken = default)
        => await db.McpServiceTokens.AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
}
