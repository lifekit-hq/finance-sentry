using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Connections;
using FinanceSentry.Infrastructure.Connections;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using FinanceSentry.Modules.CryptoSync.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence.Repositories;

// Reads run under the Owner filter. The scheduled syncs and the cross-module readers run with no person in
// scope, so they call the <c>…Unscoped…</c> methods, which opt out explicitly (a filtered existence check
// would find no existing rows and re-insert duplicates); the sync upsert and trade dedup do the same
// internally. Each opted-out query keeps its own UserId predicate (or an explicit all-active sweep).
public sealed class ExchangeCredentialRepository(CryptoSyncDbContext context) : IExchangeCredentialRepository
{
    private readonly CryptoSyncDbContext _context = context;

    public async Task AddAsync(ExchangeCredential credential, CancellationToken ct = default)
    {
        await _context.ExchangeCredentials.AddAsync(credential, ct);
    }

    public async Task<ExchangeCredential?> GetAsync(Guid userId, string provider, CancellationToken ct = default)
    {
        return await _context.ExchangeCredentials
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Provider == provider, ct);
    }

    public async Task<ExchangeCredential?> GetUnscopedAsync(Guid userId, string provider, CancellationToken ct = default)
    {
        return await _context.ExchangeCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Provider == provider, ct);
    }

    public async Task<IReadOnlyList<ExchangeCredential>> GetAllActiveUnscopedAsync(string provider, CancellationToken ct = default)
    {
        return await _context.ExchangeCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(c => c.IsActive && c.Provider == provider)
            .ToListAsync(ct);
    }

    public async Task SaveHealthUnscopedAsync(Guid credentialId, ConnectionHealth health, CancellationToken ct = default)
    {
        await _context.ExchangeCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(c => c.Id == credentialId)
            .ExecuteUpdateAsync(s => s.SetConnectionHealth(c => c.Health, health), ct);
    }

    public void Update(ExchangeCredential credential)
    {
        _context.ExchangeCredentials.Update(credential);
    }

    public void Delete(ExchangeCredential credential)
    {
        _context.ExchangeCredentials.Remove(credential);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
    }
}

public sealed class CryptoHoldingRepository(CryptoSyncDbContext context) : ICryptoHoldingRepository
{
    private readonly CryptoSyncDbContext _context = context;

    public async Task UpsertRangeAsync(IReadOnlyList<CryptoHolding> holdings, CancellationToken ct = default)
    {
        foreach (var holding in holdings)
        {
            var existing = await _context.CryptoHoldings.IgnoreQueryFilters([OwnerQueryFilter.Name])
                .FirstOrDefaultAsync(h => h.UserId == holding.UserId
                    && h.Provider == holding.Provider
                    && h.Asset == holding.Asset, ct);

            if (existing is not null)
            {
                existing.Update(holding.FreeQuantity, holding.LockedQuantity, holding.UsdValue, holding.IsFiat);
                _context.CryptoHoldings.Update(existing);
            }
            else
            {
                await _context.CryptoHoldings.AddAsync(holding, ct);
            }
        }
    }

    public async Task<IReadOnlyList<CryptoHolding>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        return await _context.CryptoHoldings
            .Where(h => h.UserId == userId && h.ClosedAt == null)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CryptoHolding>> GetByUserIdUnscopedAsync(Guid userId, CancellationToken ct = default)
    {
        return await _context.CryptoHoldings.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(h => h.UserId == userId && h.ClosedAt == null)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CryptoHolding>> GetByUserAndProviderUnscopedAsync(
        Guid userId, string provider, CancellationToken ct = default)
    {
        return await _context.CryptoHoldings.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(h => h.UserId == userId && h.Provider == provider && h.ClosedAt == null)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CryptoHolding>> GetAllByUserAndProviderAsync(
        Guid userId, string provider, CancellationToken ct = default)
    {
        return await _context.CryptoHoldings
            .Where(h => h.UserId == userId && h.Provider == provider)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CryptoHolding>> GetAllByUserAndProviderUnscopedAsync(
        Guid userId, string provider, CancellationToken ct = default)
    {
        return await _context.CryptoHoldings.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(h => h.UserId == userId && h.Provider == provider)
            .ToListAsync(ct);
    }

    public void RemoveRange(IEnumerable<CryptoHolding> holdings)
    {
        _context.CryptoHoldings.RemoveRange(holdings);
    }

    public async Task DeleteByUserAndProviderAsync(Guid userId, string provider, CancellationToken ct = default)
    {
        await _context.CryptoHoldings
            .Where(h => h.UserId == userId && h.Provider == provider)
            .ExecuteDeleteAsync(ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
    }
}

public sealed class CryptoTradeRepository(CryptoSyncDbContext context) : ICryptoTradeRepository
{
    private readonly CryptoSyncDbContext _context = context;

    public async Task AddNewAsync(
        Guid userId, string provider, IReadOnlyList<CryptoTrade> trades, CancellationToken ct = default)
    {
        if (trades.Count == 0)
        {
            return;
        }

        var tradeIds = trades.Select(t => t.TradeId).Distinct(StringComparer.Ordinal).ToList();
        var existingTradeIds = await _context.CryptoTrades.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(t => t.UserId == userId && t.Provider == provider && tradeIds.Contains(t.TradeId))
            .Select(t => t.TradeId)
            .ToListAsync(ct);
        var existing = existingTradeIds.ToHashSet(StringComparer.Ordinal);

        var toAdd = trades
            .Where(t => existing.Add(t.TradeId))
            .Select(t => CryptoTradeRecord.Create(userId, provider, t))
            .ToList();

        if (toAdd.Count > 0)
        {
            await _context.CryptoTrades.AddRangeAsync(toAdd, ct);
        }
    }
}
