namespace FinanceSentry.Modules.BankSync.Domain.Repositories;

using FinanceSentry.Core.Connections;
using FinanceSentry.Modules.BankSync.Domain;

/// <summary>
/// Repository interface for BankAccount aggregate root operations.
/// </summary>
public interface IBankAccountRepository
{
    /// <summary>
    /// Add a new bank account.
    /// </summary>
    Task<BankAccount> AddAsync(BankAccount account, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get bank account by ID. Runs under the Owner query filter: a request only finds its own accounts.
    /// </summary>
    Task<BankAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get bank account by ID for the no-person callers (scheduled sync, sync-completion handler).
    /// Opts out of the Owner query filter.
    /// </summary>
    Task<BankAccount?> GetByIdUnscopedAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get bank account by its provider-side external account ID. For the anonymous provider callback;
    /// opts out of the Owner query filter.
    /// </summary>
    Task<BankAccount?> GetByExternalAccountIdUnscopedAsync(string externalAccountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether any row — active or soft-deleted, of any user — already holds this provider-side
    /// external account ID (the unique index covers both). Opts out of the Owner query filter.
    /// </summary>
    Task<bool> ExistsByExternalAccountIdUnscopedAsync(string externalAccountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all accounts for a user. Runs under the Owner query filter.
    /// </summary>
    Task<IEnumerable<BankAccount>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all accounts for a user for the no-person callers (cross-module readers, background jobs).
    /// Opts out of the Owner query filter and keeps the explicit user predicate.
    /// </summary>
    Task<IEnumerable<BankAccount>> GetByUserIdUnscopedAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update existing bank account.
    /// </summary>
    Task<BankAccount> UpdateAsync(BankAccount account, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores an account's connection health alone, straight to the row: the shadow recorder runs after the sync's own
    /// save, whatever state that left the change tracker in. For the no-person sync; opts out of the Owner query filter.
    /// </summary>
    Task SaveHealthUnscopedAsync(Guid accountId, ConnectionHealth health, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete bank account (soft delete by setting IsActive = false).
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hard-remove the bank account row from the DB (used by the institution
    /// disconnect flow so the parent credential/connection can be removed
    /// without a dangling FK).
    /// </summary>
    Task<bool> HardDeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claims the account for a sync: one conditional UPDATE moves <c>SyncStatus</c> to
    /// "syncing" unless it already is, so of any number of concurrent callers exactly one gets true.
    /// False when another sync holds the account (or it is missing or inactive). Opts out of the Owner
    /// query filter; the claim is released by the sync's own completion or by <c>StaleSyncReaperJob</c>.
    /// </summary>
    Task<bool> TryClaimSyncUnscopedAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a claim: one conditional UPDATE moves <c>SyncStatus</c> from "syncing" to "active" and clears the
    /// last error. It bypasses the change tracker, so a context left holding pending changes by a failed save
    /// cannot block it. Opts out of the Owner query filter.
    /// </summary>
    Task ReleaseSyncUnscopedAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get accounts of every user with specific sync status. Opts out of the Owner query filter.
    /// </summary>
    Task<IEnumerable<BankAccount>> GetBySyncStatusUnscopedAsync(string status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get every user's active (IsActive=true) accounts regardless of sync status, except fake
    /// <see cref="BankAccount.SeededProvider"/> accounts, so a seeded user is invisible to the scheduler, the
    /// cross-user jobs and the cross-module readers. Opts out of the Owner query filter.
    /// </summary>
    Task<IEnumerable<BankAccount>> GetAllActiveUnscopedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Save changes to database.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Repository interface for Transaction operations.
/// </summary>
public interface ITransactionRepository
{
    /// <summary>
    /// Add a new transaction.
    /// </summary>
    Task<Transaction> AddAsync(Transaction transaction, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add multiple transactions in batch.
    /// </summary>
    Task<IEnumerable<Transaction>> AddRangeAsync(IEnumerable<Transaction> transactions, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get transaction by ID.
    /// </summary>
    Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all transactions for an account for the scheduled sync. Opts out of the Owner query filter.
    /// </summary>
    Task<IEnumerable<Transaction>> GetByAccountIdUnscopedAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get transactions by account ID with pagination.
    /// </summary>
    Task<IEnumerable<Transaction>> GetByAccountIdAsync(Guid accountId, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get transactions posted after a specific date.
    /// </summary>
    Task<IEnumerable<Transaction>> GetByAccountIdAndDateAsync(Guid accountId, DateTime desde, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if transaction with unique hash already exists for account.
    /// </summary>
    Task<bool> ExistsByUniqueHashAsync(Guid accountId, string uniqueHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// All UniqueHash values for the account, <b>including soft-deleted (IsActive=false) rows</b>.
    /// Bypasses the global IsActive query filter so dedup catches hashes that still occupy the
    /// unique index (AccountId, UniqueHash); otherwise re-syncing a previously soft-deleted
    /// transaction violates the constraint and poisons the whole batch. Also opts out of the Owner
    /// query filter, as the scheduled sync has no person in scope.
    /// </summary>
    Task<IReadOnlyCollection<string>> GetAllUniqueHashesByAccountIdUnscopedAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get transaction count for an account.
    /// </summary>
    Task<int> CountByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all transactions for a user across all accounts. Runs under the Owner query filter.
    /// </summary>
    Task<IEnumerable<Transaction>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all transactions for a user across all accounts for the no-person callers (cross-module
    /// readers, background jobs). Opts out of the Owner query filter and keeps the explicit user predicate.
    /// </summary>
    Task<IEnumerable<Transaction>> GetByUserIdUnscopedAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all transactions for a user posted or occurring on/after the given date. Runs under the
    /// Owner query filter.
    /// </summary>
    Task<IEnumerable<Transaction>> GetByUserIdSinceAsync(Guid userId, DateTime since, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all transactions for a user posted or occurring on/after the given date for the no-person
    /// callers (cross-module readers, background jobs). Opts out of the Owner query filter and keeps the
    /// explicit user predicate.
    /// </summary>
    Task<IEnumerable<Transaction>> GetByUserIdSinceUnscopedAsync(Guid userId, DateTime since, CancellationToken cancellationToken = default);

    /// <summary>
    /// Composed, server-side filtered read for the global ledger: applies every dimension of
    /// <paramref name="filter"/> in SQL (account, category, date range, amount, search, type),
    /// orders by <c>PostedDate ?? TransactionDate</c> descending, and pages the result. The
    /// returned count reflects the filtered set, not the user's whole transaction table.
    /// </summary>
    Task<(IReadOnlyList<Transaction> Items, int TotalCount)> GetFilteredByUserIdAsync(
        Guid userId, TransactionFilter filter, int offset, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Composed, server-side filtered read scoped to one account (per-account transaction page).
    /// Same filter dimensions as <see cref="GetFilteredByUserIdAsync"/> minus account selection,
    /// which is fixed by <paramref name="accountId"/>.
    /// </summary>
    Task<(IReadOnlyList<Transaction> Items, int TotalCount)> GetFilteredByAccountIdAsync(
        Guid accountId, TransactionFilter filter, int offset, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes all transactions for an account (sets IsActive=false) for account removal flow.
    /// Uses IgnoreQueryFilters() internally to find already-inactive rows (idempotent).
    /// </summary>
    Task SoftDeleteByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Save changes to database.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Repository interface for SyncJob operations.
/// </summary>
public interface ISyncJobRepository
{
    /// <summary>
    /// Add a new sync job.
    /// </summary>
    Task<SyncJob> AddAsync(SyncJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get sync job by ID. Runs under the Owner query filter.
    /// </summary>
    Task<SyncJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all sync jobs for an account.
    /// </summary>
    Task<IEnumerable<SyncJob>> GetByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the most recent sync job for an account.
    /// </summary>
    Task<SyncJob?> GetLatestByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get every user's sync jobs with specific status. Opts out of the Owner query filter.
    /// </summary>
    Task<IEnumerable<SyncJob>> GetByStatusUnscopedAsync(string status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update sync job.
    /// </summary>
    Task<SyncJob> UpdateAsync(SyncJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete sync job (hard delete, safe for job records).
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the most recent successful sync job for any account owned by the user.
    /// Returns null if no successful sync has ever completed for the user.
    /// </summary>
    Task<SyncJob?> GetLatestSuccessfulByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The completion time of the most recent <b>successful</b> sync per account for the user
    /// (accountId → CompletedAt). Accounts that have never synced successfully are absent.
    /// Distinct from an account's last sync <i>attempt</i> — used to surface how stale the data
    /// really is when recent attempts have been failing.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, DateTime>> GetLastSuccessfulSyncTimesByUserAsync(
        Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// <see cref="GetLastSuccessfulSyncTimesByUserAsync"/> for the no-person callers (cross-module
    /// readers). Opts out of the Owner query filter and keeps the explicit user predicate.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, DateTime>> GetLastSuccessfulSyncTimesByUserUnscopedAsync(
        Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Save changes to database.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IMonobankCredentialRepository
{
    Task<MonobankCredential> AddAsync(MonobankCredential credential, CancellationToken cancellationToken = default);
    Task<MonobankCredential?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Credential by ID for the scheduled sync, which has no person in scope. Opts out of the Owner query filter.
    /// </summary>
    Task<MonobankCredential?> GetByIdUnscopedAsync(Guid id, CancellationToken cancellationToken = default);
    Task<MonobankCredential?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// All stored Monobank credentials of every user. Used by the account-discovery pass, which re-lists
    /// provider accounts for every connected Monobank token. Opts out of the Owner query filter.
    /// </summary>
    Task<IReadOnlyList<MonobankCredential>> GetAllUnscopedAsync(CancellationToken cancellationToken = default);
    Task<MonobankCredential> UpdateAsync(MonobankCredential credential, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Repository for counterparty definitions and their match rules.
/// </summary>
public interface ICounterpartyRepository
{
    /// <summary>
    /// Returns counterparties that apply to <paramref name="userId"/>: those owned by the
    /// user plus system defaults (UserId == Guid.Empty), with Rules eagerly loaded.
    /// </summary>
    Task<IReadOnlyList<Counterparty>> GetForUserAsync(
        Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Returns the counterparty by id, or null when it does not exist.</summary>
    Task<Counterparty?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Persists changes made to a previously-fetched counterparty.</summary>
    Task<Counterparty> UpdateAsync(Counterparty counterparty, CancellationToken cancellationToken = default);
}

public interface ITrueLayerConnectionRepository
{
    Task<TrueLayerConnection> AddAsync(TrueLayerConnection connection, CancellationToken cancellationToken = default);
    Task<TrueLayerConnection?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Connection by ID for the token refresh and the scheduled sync, which have no person in scope.
    /// Opts out of the Owner query filter.
    /// </summary>
    Task<TrueLayerConnection?> GetByIdUnscopedAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Connection by its connect-flow reference, for the anonymous provider callback. Opts out of the
    /// Owner query filter.
    /// </summary>
    Task<TrueLayerConnection?> GetByReferenceUnscopedAsync(string reference, CancellationToken cancellationToken = default);
    Task<TrueLayerConnection?> GetByUserAndProviderAsync(Guid userId, string providerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TrueLayerConnection>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// All LINKED connections of every user. Used by the account-discovery pass to re-list provider accounts
    /// for every connection that has a usable refresh token. Opts out of the Owner query filter.
    /// </summary>
    Task<IReadOnlyList<TrueLayerConnection>> GetAllLinkedUnscopedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// LINKED connections whose consent expires on/before <paramref name="threshold"/> (and hasn't
    /// already lapsed), of every user — the pre-expiry reminder detector uses this to nudge the user to
    /// reconnect. Opts out of the Owner query filter.
    /// </summary>
    Task<IReadOnlyList<TrueLayerConnection>> GetLinkedExpiringBeforeUnscopedAsync(DateTime threshold, CancellationToken cancellationToken = default);
    Task<TrueLayerConnection> UpdateAsync(TrueLayerConnection connection, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
