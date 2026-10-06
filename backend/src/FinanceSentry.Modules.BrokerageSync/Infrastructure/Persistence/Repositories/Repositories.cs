using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence.Repositories;

// Reads run under the Owner filter. The scheduled syncs and the cross-module readers run with no person in
// scope, so they call the <c>…Unscoped…</c> methods, which opt out explicitly (a filtered existence check
// would find no existing rows and re-insert duplicates); the sync upsert does the same internally. Each
// opted-out query keeps its own UserId predicate (or an explicit all-active sweep).
public sealed class IBKRCredentialRepository : IIBKRCredentialRepository
{
    private readonly BrokerageSyncDbContext _context;

    public IBKRCredentialRepository(BrokerageSyncDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(IBKRCredential credential, CancellationToken ct = default)
    {
        await _context.IBKRCredentials.AddAsync(credential, ct);
    }

    public async Task<IBKRCredential?> GetByIdUnscopedAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.IBKRCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<IBKRCredential?> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        return await _context.IBKRCredentials
            .FirstOrDefaultAsync(c => c.UserId == userId, ct);
    }

    public async Task<IBKRCredential?> GetByUserIdUnscopedAsync(Guid userId, CancellationToken ct = default)
    {
        return await _context.IBKRCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .FirstOrDefaultAsync(c => c.UserId == userId, ct);
    }

    public async Task<IReadOnlyList<IBKRCredential>> GetAllActiveUnscopedAsync(CancellationToken ct = default)
    {
        return await _context.IBKRCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(c => c.IsActive)
            .ToListAsync(ct);
    }

    public void Update(IBKRCredential credential)
    {
        _context.IBKRCredentials.Update(credential);
    }

    public void Delete(IBKRCredential credential)
    {
        _context.IBKRCredentials.Remove(credential);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
    }
}

public sealed class IBKRFlexCredentialRepository : IIBKRFlexCredentialRepository
{
    private readonly BrokerageSyncDbContext _context;

    public IBKRFlexCredentialRepository(BrokerageSyncDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(IBKRFlexCredential credential, CancellationToken ct = default)
    {
        await _context.IBKRFlexCredentials.AddAsync(credential, ct);
    }

    public async Task<IBKRFlexCredential?> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        return await _context.IBKRFlexCredentials
            .FirstOrDefaultAsync(c => c.UserId == userId, ct);
    }

    public async Task<IBKRFlexCredential?> GetByUserIdUnscopedAsync(Guid userId, CancellationToken ct = default)
    {
        return await _context.IBKRFlexCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .FirstOrDefaultAsync(c => c.UserId == userId, ct);
    }

    public async Task<IReadOnlyList<IBKRFlexCredential>> GetAllActiveUnscopedAsync(CancellationToken ct = default)
    {
        return await _context.IBKRFlexCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(c => c.IsActive)
            .ToListAsync(ct);
    }

    public void Update(IBKRFlexCredential credential)
    {
        _context.IBKRFlexCredentials.Update(credential);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
    }

    public async Task SaveLastErrorUnscopedAsync(IBKRFlexCredential credential, CancellationToken ct = default)
    {
        var lastError = credential.LastError;
        await _context.IBKRFlexCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(c => c.Id == credential.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastError, lastError), ct);
    }
}

public sealed class BrokerageHoldingRepository : IBrokerageHoldingRepository
{
    private readonly BrokerageSyncDbContext _context;

    public BrokerageHoldingRepository(BrokerageSyncDbContext context)
    {
        _context = context;
    }

    public async Task UpsertRangeAsync(IEnumerable<BrokerageHolding> holdings, CancellationToken ct = default)
    {
        foreach (var holding in holdings)
        {
            var existing = await _context.BrokerageHoldings.IgnoreQueryFilters([OwnerQueryFilter.Name])
                .FirstOrDefaultAsync(
                    h => h.UserId == holding.UserId
                        && h.Symbol == holding.Symbol
                        && h.Provider == holding.Provider,
                    ct);

            if (existing is not null)
            {
                existing.Update(holding.Quantity, holding.UsdValue, holding.InstrumentId, holding.FlexAsOfDate);
                _context.BrokerageHoldings.Update(existing);
            }
            else
            {
                await _context.BrokerageHoldings.AddAsync(holding, ct);
            }
        }
    }

    public async Task<IReadOnlyList<BrokerageHolding>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        return await _context.BrokerageHoldings
            .Where(h => h.UserId == userId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<BrokerageHolding>> GetByUserIdUnscopedAsync(Guid userId, CancellationToken ct = default)
    {
        return await _context.BrokerageHoldings.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(h => h.UserId == userId)
            .ToListAsync(ct);
    }

    public void RemoveRange(IEnumerable<BrokerageHolding> holdings)
    {
        _context.BrokerageHoldings.RemoveRange(holdings);
    }

    public async Task DeleteByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        await _context.BrokerageHoldings
            .Where(h => h.UserId == userId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
    }
}

public sealed class BrokerageTradeRepository : IBrokerageTradeRepository
{
    private readonly BrokerageSyncDbContext _context;

    public BrokerageTradeRepository(BrokerageSyncDbContext context)
    {
        _context = context;
    }

    public async Task<BrokerageTrade?> GetByExecutionIdUnscopedAsync(
        Guid userId, string provider, string ibExecutionId, CancellationToken ct = default)
    {
        return await _context.BrokerageTrades.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .FirstOrDefaultAsync(
                t => t.UserId == userId && t.Provider == provider && t.IbExecutionId == ibExecutionId,
                ct);
    }

    public async Task AddAsync(BrokerageTrade trade, CancellationToken ct = default)
    {
        await _context.BrokerageTrades.AddAsync(trade, ct);
    }

    public void Update(BrokerageTrade trade)
    {
        _context.BrokerageTrades.Update(trade);
    }

    public async Task<IReadOnlyList<BrokerageTrade>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        return await _context.BrokerageTrades
            .Where(t => t.UserId == userId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<BrokerageTrade>> GetByUserIdUnscopedAsync(Guid userId, CancellationToken ct = default)
    {
        return await _context.BrokerageTrades.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(t => t.UserId == userId)
            .ToListAsync(ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
    }
}

public sealed class BrokerageCashTransactionRepository : IBrokerageCashTransactionRepository
{
    private readonly BrokerageSyncDbContext _context;

    public BrokerageCashTransactionRepository(BrokerageSyncDbContext context)
    {
        _context = context;
    }

    public async Task<BrokerageCashTransaction?> GetByIdempotencyKeyUnscopedAsync(
        Guid userId, string provider, string idempotencyKey, CancellationToken ct = default)
    {
        return await _context.BrokerageCashTransactions.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .FirstOrDefaultAsync(
                c => c.UserId == userId && c.Provider == provider && c.IdempotencyKey == idempotencyKey,
                ct);
    }

    public async Task AddAsync(BrokerageCashTransaction transaction, CancellationToken ct = default)
    {
        await _context.BrokerageCashTransactions.AddAsync(transaction, ct);
    }

    public async Task<IReadOnlyList<BrokerageCashTransaction>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        return await _context.BrokerageCashTransactions
            .Where(c => c.UserId == userId)
            .ToListAsync(ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
    }
}

public sealed class BrokerageInstrumentRepository : IBrokerageInstrumentRepository
{
    private readonly BrokerageSyncDbContext _context;

    public BrokerageInstrumentRepository(BrokerageSyncDbContext context)
    {
        _context = context;
    }

    public async Task<BrokerageInstrument?> GetByConidUnscopedAsync(
        Guid userId, string provider, long conid, CancellationToken ct = default)
    {
        return await _context.BrokerageInstruments.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .FirstOrDefaultAsync(
                i => i.UserId == userId && i.Provider == provider && i.Conid == conid,
                ct);
    }

    public async Task<BrokerageInstrument?> GetByIdAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        return await _context.BrokerageInstruments
            .FirstOrDefaultAsync(i => i.UserId == userId && i.Id == id, ct);
    }

    public async Task<IReadOnlyList<BrokerageInstrument>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        return await _context.BrokerageInstruments
            .Where(i => i.UserId == userId)
            .ToListAsync(ct);
    }

    public async Task AddAsync(BrokerageInstrument instrument, CancellationToken ct = default)
    {
        await _context.BrokerageInstruments.AddAsync(instrument, ct);
    }

    public void Update(BrokerageInstrument instrument)
    {
        _context.BrokerageInstruments.Update(instrument);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
    }
}
