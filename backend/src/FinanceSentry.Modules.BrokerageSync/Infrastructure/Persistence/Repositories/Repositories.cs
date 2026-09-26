using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence.Repositories;

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

    public async Task<IBKRCredential?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.IBKRCredentials
            .FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<IBKRCredential?> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        return await _context.IBKRCredentials
            .FirstOrDefaultAsync(c => c.UserId == userId, ct);
    }

    public async Task<IReadOnlyList<IBKRCredential>> GetAllActiveAsync(CancellationToken ct = default)
    {
        return await _context.IBKRCredentials
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

    public async Task<IReadOnlyList<IBKRFlexCredential>> GetAllActiveAsync(CancellationToken ct = default)
    {
        return await _context.IBKRFlexCredentials
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
            var existing = await _context.BrokerageHoldings
                .FirstOrDefaultAsync(
                    h => h.UserId == holding.UserId
                        && h.Symbol == holding.Symbol
                        && h.Provider == holding.Provider,
                    ct);

            if (existing is not null)
            {
                existing.Update(holding.Quantity, holding.UsdValue, holding.InstrumentId);
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

public sealed class BrokerageInstrumentRepository : IBrokerageInstrumentRepository
{
    private readonly BrokerageSyncDbContext _context;

    public BrokerageInstrumentRepository(BrokerageSyncDbContext context)
    {
        _context = context;
    }

    public async Task<BrokerageInstrument?> GetByConidAsync(
        Guid userId, string provider, long conid, CancellationToken ct = default)
    {
        return await _context.BrokerageInstruments
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
