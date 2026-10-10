namespace FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Connections;
using FinanceSentry.Infrastructure.Connections;
using Microsoft.EntityFrameworkCore;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;

/// <summary>
/// Entity Framework Core implementation of IBankAccountRepository. Reads run under the context's Owner
/// query filter. The scheduled sync, the stale-sync reaper, the credential backup, the provider callback
/// and the cross-module readers run with no person in scope, so they call the <c>…Unscoped…</c> methods,
/// which opt out through <see cref="AllUsers"/>; their own predicates keep them scoped.
/// </summary>
public class BankAccountRepository(BankSyncDbContext context) : IBankAccountRepository
{
    private readonly BankSyncDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    private IQueryable<BankAccount> AllUsers => _context.BankAccounts.IgnoreQueryFilters([OwnerQueryFilter.Name]);

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

    public async Task<BankAccount?> GetByIdUnscopedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await AllUsers
            .FirstOrDefaultAsync(ba => ba.Id == id && ba.IsActive, cancellationToken);
    }

    public async Task<BankAccount?> GetByExternalAccountIdUnscopedAsync(string externalAccountId, CancellationToken cancellationToken = default)
    {
        return await AllUsers
            .FirstOrDefaultAsync(ba => ba.ExternalAccountId == externalAccountId && ba.IsActive, cancellationToken);
    }

    public async Task<bool> ExistsByExternalAccountIdUnscopedAsync(string externalAccountId, CancellationToken cancellationToken = default)
    {
        // External account ids are unique across users, so the duplicate check must see every user's rows.
        return await AllUsers
            .AnyAsync(ba => ba.ExternalAccountId == externalAccountId, cancellationToken);
    }

    public async Task<IEnumerable<BankAccount>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.BankAccounts
            .Where(ba => ba.UserId == userId && ba.IsActive)
            .OrderByDescending(ba => ba.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<BankAccount>> GetByUserIdUnscopedAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await AllUsers
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

    public async Task SaveHealthUnscopedAsync(Guid accountId, ConnectionHealth health, CancellationToken cancellationToken = default)
    {
        await AllUsers
            .Where(ba => ba.Id == accountId)
            .ExecuteUpdateAsync(s => s.SetConnectionHealth(ba => ba.Health, health), cancellationToken);
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

    public async Task<bool> TryClaimSyncUnscopedAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var claimed = await AllUsers
            .Where(ba => ba.Id == accountId && ba.IsActive && ba.SyncStatus != "syncing")
            .ExecuteUpdateAsync(
                s => s.SetProperty(ba => ba.SyncStatus, "syncing").SetProperty(ba => ba.UpdatedAt, DateTime.UtcNow),
                cancellationToken);

        // ExecuteUpdate bypasses the change tracker; refresh an instance this scope already loaded
        // (the coordinator reads the account first) so it does not report the pre-claim status.
        if (claimed == 1)
        {
            foreach (var entry in _context.ChangeTracker.Entries<BankAccount>().Where(e => e.Entity.Id == accountId).ToList())
                await entry.ReloadAsync(cancellationToken);
        }

        return claimed == 1;
    }

    public async Task ReleaseSyncUnscopedAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await AllUsers
            .Where(ba => ba.Id == accountId && ba.SyncStatus == "syncing")
            .ExecuteUpdateAsync(
                s => s.SetProperty(ba => ba.SyncStatus, "active")
                    .SetProperty(ba => ba.LastSyncError, (string?)null)
                    .SetProperty(ba => ba.UpdatedAt, DateTime.UtcNow),
                cancellationToken);
    }

    public async Task<IEnumerable<BankAccount>> GetBySyncStatusUnscopedAsync(string status, CancellationToken cancellationToken = default)
    {
        return await AllUsers
            .Where(ba => ba.SyncStatus == status && ba.IsActive)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<BankAccount>> GetAllActiveUnscopedAsync(CancellationToken cancellationToken = default)
    {
        return await AllUsers
            .Where(ba => ba.IsActive && ba.Provider != BankAccount.SeededProvider)
            .OrderBy(ba => ba.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Entity Framework Core implementation of ITransactionRepository. Reads run under the context's Owner
/// query filter. The scheduled sync and the cross-module readers run with no person in scope, so they call
/// the <c>…Unscoped…</c> methods, which opt out through <see cref="AllUsers"/>; their own predicates keep
/// them scoped. Paths that also need archived rows opt out of the soft-delete filter by name instead of
/// dropping every filter.
/// </summary>
public class TransactionRepository(BankSyncDbContext context) : ITransactionRepository
{
    private readonly BankSyncDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    private IQueryable<Transaction> AllUsers => _context.Transactions.IgnoreQueryFilters([OwnerQueryFilter.Name]);

    private IQueryable<Transaction> AllUsersIncludingArchived => _context.Transactions
        .IgnoreQueryFilters([OwnerQueryFilter.Name, BankSyncDbContext.ActiveFilterName]);

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

    public async Task<IEnumerable<Transaction>> GetByAccountIdUnscopedAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return await AllUsers
            .Where(t => t.AccountId == accountId)
            .OrderByDescending(t => t.PostedDate ?? t.TransactionDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<string>> GetAllUniqueHashesByAccountIdUnscopedAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        // Opting out of the soft-delete filter (and, as the sync job has no person in scope, the Owner
        // filter) so soft-deleted rows are included. Their hashes still occupy the unique index (AccountId, UniqueHash); leaving
        // them out of the dedup set lets a re-synced transaction re-insert and violate the
        // constraint, which poisons the whole SaveChanges batch.
        return await AllUsersIncludingArchived
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

    public async Task<IEnumerable<Transaction>> GetByUserIdUnscopedAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await AllUsers
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

    public async Task<IEnumerable<Transaction>> GetByUserIdSinceUnscopedAsync(Guid userId, DateTime since, CancellationToken cancellationToken = default)
    {
        return await AllUsers
            .Where(t => t.UserId == userId && (t.PostedDate >= since || t.TransactionDate >= since))
            .OrderByDescending(t => t.PostedDate ?? t.TransactionDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<Transaction> Items, int TotalCount)> GetFilteredByUserIdAsync(
        Guid userId, TransactionFilter filter, int offset, int limit, CancellationToken cancellationToken = default)
    {
        var query = ApplyFilter(_context.Transactions.Where(t => t.UserId == userId), filter);
        return await PageAsync(query, offset, limit, cancellationToken);
    }

    public async Task<(IReadOnlyList<Transaction> Items, int TotalCount)> GetFilteredByAccountIdAsync(
        Guid accountId, TransactionFilter filter, int offset, int limit, CancellationToken cancellationToken = default)
    {
        var query = ApplyFilter(_context.Transactions.Where(t => t.AccountId == accountId), filter);
        return await PageAsync(query, offset, limit, cancellationToken);
    }

    private static async Task<(IReadOnlyList<Transaction> Items, int TotalCount)> PageAsync(
        IQueryable<Transaction> query, int offset, int limit, CancellationToken cancellationToken)
    {
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(t => t.PostedDate ?? t.TransactionDate)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return (items, totalCount);
    }

    /// <summary>
    /// Applies every dimension of <paramref name="filter"/> in SQL. The date bound is the house
    /// "PostedDate falls back to TransactionDate" predicate (renders as COALESCE); search covers
    /// Description and MerchantName via ILIKE (<c>NewsRepository</c> precedent). Amount is split
    /// into two shapes: a single native <see cref="TransactionFilter.MinAmount"/>/<see cref="TransactionFilter.MaxAmount"/>
    /// pair for a single-currency scope (the per-account query), or <see cref="TransactionFilter.AmountRanges"/> —
    /// one native bound per currency group — for the multi-currency global ledger, unioned so no
    /// native amount is ever compared across currencies.
    /// </summary>
    private static IQueryable<Transaction> ApplyFilter(IQueryable<Transaction> query, TransactionFilter filter)
    {
        if (filter.AccountIds is { Count: > 0 } accountIds)
            query = query.Where(t => accountIds.Contains(t.AccountId));

        if (filter.Categories is { Count: > 0 } categories)
        {
            var includesUncategorized = categories.Contains(CategoryKeys.Uncategorized);
            var classifiedIds = filter.CategoryTransactionIds ?? [];
            query = query.Where(t =>
                (t.MerchantCategory != null && categories.Contains(t.MerchantCategory))
                || (includesUncategorized && t.MerchantCategory == null)
                || classifiedIds.Contains(t.Id));
        }

        if (filter.From.HasValue)
            query = query.Where(t => (t.PostedDate ?? t.TransactionDate) >= filter.From.Value);

        if (filter.To.HasValue)
            query = query.Where(t => (t.PostedDate ?? t.TransactionDate) <= filter.To.Value);

        if (!string.IsNullOrEmpty(filter.TransactionType))
            query = query.Where(t => t.TransactionType == filter.TransactionType);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var like = $"%{filter.Search.Trim()}%";
            query = query.Where(t => EF.Functions.ILike(t.Description, like) || EF.Functions.ILike(t.MerchantName ?? "", like));
        }

        if (filter.MinAmount.HasValue)
            query = query.Where(t => t.Amount >= filter.MinAmount.Value);

        if (filter.MaxAmount.HasValue)
            query = query.Where(t => t.Amount <= filter.MaxAmount.Value);

        if (filter.AmountRanges is { Count: > 0 } ranges)
        {
            IQueryable<Transaction>? unioned = null;
            foreach (var range in ranges)
            {
                var groupIds = range.AccountIds;
                var group = query.Where(t => groupIds.Contains(t.AccountId));
                if (range.Min.HasValue)
                    group = group.Where(t => t.Amount >= range.Min.Value);
                if (range.Max.HasValue)
                    group = group.Where(t => t.Amount <= range.Max.Value);
                unioned = unioned is null ? group : unioned.Union(group);
            }
            query = unioned ?? query.Where(_ => false);
        }

        return query;
    }

    public async Task SoftDeleteByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        // Opting out of the IsActive filter covers already-inactive rows too, making this operation
        // idempotent; the Owner filter stays on (a request acting for another person matches nothing).
        await _context.Transactions
            .IgnoreQueryFilters([BankSyncDbContext.ActiveFilterName])
            .Where(t => t.AccountId == accountId && t.IsActive)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.IsActive, false)
                    .SetProperty(t => t.DeletedAt, now)
                    .SetProperty(t => t.ArchivedReason, "account_deleted"),
                cancellationToken);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Entity Framework Core implementation of ISyncJobRepository. Reads run under the context's Owner query
/// filter. The sync run, the stale-sync reaper and the cross-module readers have no person in scope, so
/// they call the <c>…Unscoped…</c> methods, which opt out through <see cref="AllUsers"/>; their own
/// predicates keep them scoped.
/// </summary>
public class SyncJobRepository(BankSyncDbContext context) : ISyncJobRepository
{
    private readonly BankSyncDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    private IQueryable<SyncJob> AllUsers => _context.SyncJobs.IgnoreQueryFilters([OwnerQueryFilter.Name]);

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

    public async Task<IEnumerable<SyncJob>> GetByStatusUnscopedAsync(string status, CancellationToken cancellationToken = default)
    {
        return await AllUsers
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

    public async Task<SyncJob?> GetLatestSuccessfulByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.SyncJobs
            .Where(sj => sj.UserId == userId && sj.Status == "success")
            .OrderByDescending(sj => sj.CompletedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, DateTime>> GetLastSuccessfulSyncTimesByUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => await LastSuccessfulSyncTimesAsync(_context.SyncJobs, userId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, DateTime>> GetLastSuccessfulSyncTimesByUserUnscopedAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => await LastSuccessfulSyncTimesAsync(AllUsers, userId, cancellationToken);

    private static async Task<IReadOnlyDictionary<Guid, DateTime>> LastSuccessfulSyncTimesAsync(
        IQueryable<SyncJob> jobs, Guid userId, CancellationToken cancellationToken)
    {
        var rows = await jobs
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

/// <summary>
/// Reads run under the context's Owner query filter. The sync run and account discovery have no person in
/// scope, so they call the <c>…Unscoped…</c> methods, which opt out through <see cref="AllUsers"/>; the
/// connect and disconnect flows' lookups stay filtered.
/// </summary>
public class MonobankCredentialRepository(BankSyncDbContext context) : IMonobankCredentialRepository
{
    private readonly BankSyncDbContext _context = context;

    private IQueryable<MonobankCredential> AllUsers => _context.MonobankCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name]);

    public async Task<MonobankCredential> AddAsync(MonobankCredential credential, CancellationToken cancellationToken = default)
    {
        await _context.MonobankCredentials.AddAsync(credential, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return credential;
    }

    public async Task<MonobankCredential?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _context.MonobankCredentials.FirstOrDefaultAsync(mc => mc.Id == id, cancellationToken);

    public async Task<MonobankCredential?> GetByIdUnscopedAsync(Guid id, CancellationToken cancellationToken = default)
        => await AllUsers.FirstOrDefaultAsync(mc => mc.Id == id, cancellationToken);

    public async Task<MonobankCredential?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _context.MonobankCredentials.FirstOrDefaultAsync(mc => mc.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<MonobankCredential>> GetAllUnscopedAsync(CancellationToken cancellationToken = default)
        => await AllUsers.ToListAsync(cancellationToken);

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

/// <summary>
/// Reads run under the context's Owner query filter. The sync run, token refresh, the provider callback and
/// the consent-expiry reminder have no person in scope, so they call the <c>…Unscoped…</c> methods, which
/// opt out through <see cref="AllUsers"/>; the connect and disconnect flows' lookups stay filtered.
/// </summary>
public class TrueLayerConnectionRepository(BankSyncDbContext context) : ITrueLayerConnectionRepository
{
    private readonly BankSyncDbContext _context = context;

    private IQueryable<TrueLayerConnection> AllUsers => _context.TrueLayerConnections.IgnoreQueryFilters([OwnerQueryFilter.Name]);

    public async Task<TrueLayerConnection> AddAsync(TrueLayerConnection connection, CancellationToken cancellationToken = default)
    {
        await _context.TrueLayerConnections.AddAsync(connection, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return connection;
    }

    public async Task<TrueLayerConnection?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _context.TrueLayerConnections.FirstOrDefaultAsync(tc => tc.Id == id, cancellationToken);

    public async Task<TrueLayerConnection?> GetByIdUnscopedAsync(Guid id, CancellationToken cancellationToken = default)
        => await AllUsers.FirstOrDefaultAsync(tc => tc.Id == id, cancellationToken);

    public async Task<TrueLayerConnection?> GetByReferenceUnscopedAsync(string reference, CancellationToken cancellationToken = default)
        => await AllUsers.FirstOrDefaultAsync(tc => tc.Reference == reference, cancellationToken);

    public async Task<TrueLayerConnection?> GetByUserAndProviderAsync(Guid userId, string providerId, CancellationToken cancellationToken = default)
        => await _context.TrueLayerConnections.FirstOrDefaultAsync(
            tc => tc.UserId == userId && tc.ProviderId == providerId, cancellationToken);

    public async Task<IReadOnlyList<TrueLayerConnection>> GetAllLinkedUnscopedAsync(CancellationToken cancellationToken = default)
        => await AllUsers
            .AsNoTracking()
            .Where(tc => tc.Status == "LINKED")
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TrueLayerConnection>> GetLinkedExpiringBeforeUnscopedAsync(DateTime threshold, CancellationToken cancellationToken = default)
        => await AllUsers
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
