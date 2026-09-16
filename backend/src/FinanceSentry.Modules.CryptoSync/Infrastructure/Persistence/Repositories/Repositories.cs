using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence.Repositories;

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

    public async Task<IReadOnlyList<ExchangeCredential>> GetAllActiveAsync(string provider, CancellationToken ct = default)
    {
        return await _context.ExchangeCredentials
            .Where(c => c.IsActive && c.Provider == provider)
            .ToListAsync(ct);
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
            var existing = await _context.CryptoHoldings
                .FirstOrDefaultAsync(h => h.UserId == holding.UserId
                    && h.Provider == holding.Provider
                    && h.Asset == holding.Asset, ct);

            if (existing is not null)
            {
                existing.Update(holding.FreeQuantity, holding.LockedQuantity, holding.UsdValue);
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
            .Where(h => h.UserId == userId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CryptoHolding>> GetByUserAndProviderAsync(
        Guid userId, string provider, CancellationToken ct = default)
    {
        return await _context.CryptoHoldings
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
