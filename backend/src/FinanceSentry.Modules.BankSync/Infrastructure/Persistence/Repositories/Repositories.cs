namespace FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;

/// <summary>
/// Entity Framework Core implementation of IBankAccountRepository.
/// </summary>
public class BankAccountRepository(BankSyncDbContext context) : IBankAccountRepository
{
    private readonly BankSyncDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<BankAccount> AddAsync(BankAccount account, CancellationToken cancellationToken = default)
    {
        account.ValidateInvariants();
        var entry = await _context.BankAccounts.AddAsync(account, cancellationToken);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            entry.State = EntityState.Detached;
            throw;
        }
        return account;
    }

    public async Task<BankAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.BankAccounts
            .FirstOrDefaultAsync(ba => ba.Id == id && ba.IsActive, cancellationToken);
    }

    public async Task<BankAccount?> GetByExternalAccountIdAsync(string externalAccountId, CancellationToken cancellationToken = default)
    {
        return await _context.BankAccounts
            .FirstOrDefaultAsync(ba => ba.ExternalAccountId == externalAccountId && ba.IsActive, cancellationToken);
    }

    public async Task<bool> ExistsByExternalAccountIdAsync(string externalAccountId, CancellationToken cancellationToken = default)
    {
        return await _context.BankAccounts
            .AnyAsync(ba => ba.ExternalAccountId == externalAccountId, cancellationToken);
    }

    public async Task<IEnumerable<BankAccount>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.BankAccounts
            .Where(ba => ba.UserId == userId && ba.IsActive)
            .OrderByDescending(ba => ba.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<BankAccount> UpdateAsync(BankAccount account, CancellationToken cancellationToken = default)
    {
        account.ValidateInvariants();
        _context.BankAccounts.Update(account);
        await _context.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await _context.BankAccounts.FirstOrDefaultAsync(ba => ba.Id == id, cancellationToken);
        if (account == null)
            return false;

        account.IsActive = false;
        _context.BankAccounts.Update(account);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> HardDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await _context.BankAccounts
            .Include(ba => ba.Transactions)
            .Include(ba => ba.SyncJobs)
            .FirstOrDefaultAsync(ba => ba.Id == id, cancellationToken);
        if (account == null)
            return false;

        _context.BankAccounts.Remove(account);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IEnumerable<BankAccount>> GetBySyncStatusAsync(string status, CancellationToken cancellationToken = default)
    {
        return await _context.BankAccounts
            .Where(ba => ba.SyncStatus == status && ba.IsActive)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<BankAccount>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        return await _context.BankAccounts
            .Where(ba => ba.IsActive)
            .OrderBy(ba => ba.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Entity Framework Core implementation of ITransactionRepository.
/// </summary>
public class TransactionRepository(BankSyncDbContext context) : ITransactionRepository
{
    private readonly BankSyncDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<Transaction> AddAsync(Transaction transaction, CancellationToken cancellationToken = default)
    {
        transaction.ValidateInvariants();
        await _context.Transactions.AddAsync(transaction, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return transaction;
    }

    public async Task<IEnumerable<Transaction>> AddRangeAsync(IEnumerable<Transaction> transactions, CancellationToken cancellationToken = default)
    {
        var txList = transactions.ToList();
        foreach (var tx in txList)
            tx.ValidateInvariants();

        await _context.Transactions.AddRangeAsync(txList, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return txList;
    }

    public async Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Transactions
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Transaction>> GetByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return await _context.Transactions
            .Where(t => t.AccountId == accountId)
            .OrderByDescending(t => t.PostedDate ?? t.TransactionDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<string>> GetAllUniqueHashesByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters() bypasses the global IsActive filter so soft-deleted rows are
        // included. Their hashes still occupy the unique index (AccountId, UniqueHash); leaving
        // them out of the dedup set lets a re-synced transaction re-insert and violate the
        // constraint, which poisons the whole SaveChanges batch.
        return await _context.Transactions
            .IgnoreQueryFilters()
            .Where(t => t.AccountId == accountId)
            .Select(t => t.UniqueHash)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Transaction>> GetByAccountIdAsync(Guid accountId, int skip, int take, CancellationToken cancellationToken = default)
    {
        return await _context.Transactions
            .Where(t => t.AccountId == accountId)
            .OrderByDescending(t => t.PostedDate ?? t.TransactionDate)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Transaction>> GetByAccountIdAndDateAsync(Guid accountId, DateTime desde, CancellationToken cancellationToken = default)
    {
        return await _context.Transactions
            .Where(t => t.AccountId == accountId && (t.PostedDate >= desde || t.TransactionDate >= desde))
            .OrderByDescending(t => t.PostedDate ?? t.TransactionDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsByUniqueHashAsync(Guid accountId, string uniqueHash, CancellationToken cancellationToken = default)
    {
        return await _context.Transactions
            .AnyAsync(t => t.AccountId == accountId && t.UniqueHash == uniqueHash, cancellationToken);
    }

    public async Task<int> CountByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return await _context.Transactions
            .CountAsync(t => t.AccountId == accountId, cancellationToken);
    }

    public async Task<IEnumerable<Transaction>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.Transactions
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.PostedDate ?? t.TransactionDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Transaction>> GetByUserIdSinceAsync(Guid userId, DateTime since, CancellationToken cancellationToken = default)
    {
        return await _context.Transactions
            .Where(t => t.UserId == userId && (t.PostedDate >= since || t.TransactionDate >= since))
            .OrderByDescending(t => t.PostedDate ?? t.TransactionDate)
            .ToListAsync(cancellationToken);
    }

    public async Task SoftDeleteByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        // IgnoreQueryFilters() bypasses the IsActive global filter so already-inactive rows
        // are also covered, making this operation idempotent.
        var transactions = await _context.Transactions
            .IgnoreQueryFilters()
            .Where(t => t.AccountId == accountId && t.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var t in transactions)
        {
            t.IsActive = false;
            t.DeletedAt = now;
            t.ArchivedReason = "account_deleted";
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Entity Framework Core implementation of ISyncJobRepository.
/// </summary>
public class SyncJobRepository(BankSyncDbContext context) : ISyncJobRepository
{
    private readonly BankSyncDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<SyncJob> AddAsync(SyncJob job, CancellationToken cancellationToken = default)
    {
        await _context.SyncJobs.AddAsync(job, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return job;
    }

    public async Task<SyncJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.SyncJobs
            .FirstOrDefaultAsync(sj => sj.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<SyncJob>> GetByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return await _context.SyncJobs
            .Where(sj => sj.AccountId == accountId)
            .OrderByDescending(sj => sj.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<SyncJob?> GetLatestByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return await _context.SyncJobs
            .Where(sj => sj.AccountId == accountId)
            .OrderByDescending(sj => sj.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IEnumerable<SyncJob>> GetByStatusAsync(string status, CancellationToken cancellationToken = default)
    {
        return await _context.SyncJobs
            .Where(sj => sj.Status == status)
            .OrderByDescending(sj => sj.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<SyncJob> UpdateAsync(SyncJob job, CancellationToken cancellationToken = default)
    {
        _context.SyncJobs.Update(job);
        await _context.SaveChangesAsync(cancellationToken);
        return job;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await _context.SyncJobs.FirstOrDefaultAsync(sj => sj.Id == id, cancellationToken);
        if (job == null)
            return false;

        _context.SyncJobs.Remove(job);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> HasRunningJobAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return await _context.SyncJobs
            .AnyAsync(sj => sj.AccountId == accountId && sj.Status == "running", cancellationToken);
    }

    public async Task<SyncJob?> GetLatestSuccessfulByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.SyncJobs
            .Where(sj => sj.UserId == userId && sj.Status == "success")
            .OrderByDescending(sj => sj.CompletedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, DateTime>> GetLastSuccessfulSyncTimesByUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var rows = await _context.SyncJobs
            .Where(sj => sj.UserId == userId && sj.Status == "success" && sj.CompletedAt != null)
            .GroupBy(sj => sj.AccountId)
            .Select(g => new {AccountId = g.Key, LastSuccess = g.Max(sj => sj.CompletedAt)})
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.AccountId, r => r.LastSuccess!.Value);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
}

public class MonobankCredentialRepository(BankSyncDbContext context) : IMonobankCredentialRepository
{
    private readonly BankSyncDbContext _context = context;

    public async Task<MonobankCredential> AddAsync(MonobankCredential credential, CancellationToken cancellationToken = default)
    {
        await _context.MonobankCredentials.AddAsync(credential, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return credential;
    }

    public async Task<MonobankCredential?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _context.MonobankCredentials.FirstOrDefaultAsync(mc => mc.Id == id, cancellationToken);

    public async Task<MonobankCredential?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _context.MonobankCredentials.FirstOrDefaultAsync(mc => mc.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<MonobankCredential>> GetAllAsync(CancellationToken cancellationToken = default)
        => await _context.MonobankCredentials.ToListAsync(cancellationToken);

    public async Task<MonobankCredential> UpdateAsync(MonobankCredential credential, CancellationToken cancellationToken = default)
    {
        _context.MonobankCredentials.Update(credential);
        await _context.SaveChangesAsync(cancellationToken);
        return credential;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var mc = await _context.MonobankCredentials.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (mc == null) return false;
        _context.MonobankCredentials.Remove(mc);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => await _context.SaveChangesAsync(cancellationToken);
}

public class TrueLayerConnectionRepository(BankSyncDbContext context) : ITrueLayerConnectionRepository
{
    private readonly BankSyncDbContext _context = context;

    public async Task<TrueLayerConnection> AddAsync(TrueLayerConnection connection, CancellationToken cancellationToken = default)
    {
        await _context.TrueLayerConnections.AddAsync(connection, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return connection;
    }

    public async Task<TrueLayerConnection?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _context.TrueLayerConnections.FirstOrDefaultAsync(tc => tc.Id == id, cancellationToken);

    public async Task<TrueLayerConnection?> GetByReferenceAsync(string reference, CancellationToken cancellationToken = default)
        => await _context.TrueLayerConnections.FirstOrDefaultAsync(tc => tc.Reference == reference, cancellationToken);

    public async Task<TrueLayerConnection?> GetByUserAndProviderAsync(Guid userId, string providerId, CancellationToken cancellationToken = default)
        => await _context.TrueLayerConnections.FirstOrDefaultAsync(
            tc => tc.UserId == userId && tc.ProviderId == providerId, cancellationToken);

    public async Task<IReadOnlyList<TrueLayerConnection>> GetAllLinkedAsync(CancellationToken cancellationToken = default)
        => await _context.TrueLayerConnections
            .Where(tc => tc.Status == "LINKED")
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TrueLayerConnection>> GetLinkedExpiringBeforeAsync(DateTime threshold, CancellationToken cancellationToken = default)
        => await _context.TrueLayerConnections
            .Where(tc => tc.Status == "LINKED"
                && tc.ConnectionExpiresAt != null
                && tc.ConnectionExpiresAt <= threshold
                && tc.ConnectionExpiresAt > DateTime.UtcNow)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TrueLayerConnection>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _context.TrueLayerConnections
            .Where(tc => tc.UserId == userId)
            .OrderByDescending(tc => tc.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<TrueLayerConnection> UpdateAsync(TrueLayerConnection connection, CancellationToken cancellationToken = default)
    {
        _context.TrueLayerConnections.Update(connection);
        await _context.SaveChangesAsync(cancellationToken);
        return connection;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tc = await _context.TrueLayerConnections.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tc == null) return false;
        _context.TrueLayerConnections.Remove(tc);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
