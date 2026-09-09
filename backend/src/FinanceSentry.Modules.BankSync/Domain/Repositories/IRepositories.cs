namespace FinanceSentry.Modules.BankSync.Domain.Repositories;

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
    /// Get bank account by ID.
    /// </summary>
    Task<BankAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get bank account by its provider-side external account ID.
    /// </summary>
    Task<BankAccount?> GetByExternalAccountIdAsync(string externalAccountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all accounts for a user.
    /// </summary>
    Task<IEnumerable<BankAccount>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update existing bank account.
    /// </summary>
    Task<BankAccount> UpdateAsync(BankAccount account, CancellationToken cancellationToken = default);

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
    /// Get accounts with specific sync status.
    /// </summary>
    Task<IEnumerable<BankAccount>> GetBySyncStatusAsync(string status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all active (IsActive=true) accounts regardless of sync status. Used by the scheduler.
    /// </summary>
    Task<IEnumerable<BankAccount>> GetAllActiveAsync(CancellationToken cancellationToken = default);

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
    /// Get all transactions for an account.
    /// </summary>
    Task<IEnumerable<Transaction>> GetByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default);

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
    /// transaction violates the constraint and poisons the whole batch.
    /// </summary>
    Task<IReadOnlyCollection<string>> GetAllUniqueHashesByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get transaction count for an account.
    /// </summary>
    Task<int> CountByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all transactions for a user across all accounts.
    /// </summary>
    Task<IEnumerable<Transaction>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all transactions for a user posted or occurring on/after the given date.
    /// </summary>
    Task<IEnumerable<Transaction>> GetByUserIdSinceAsync(Guid userId, DateTime since, CancellationToken cancellationToken = default);

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
    /// Get sync job by ID.
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
    /// Get sync jobs with specific status.
    /// </summary>
    Task<IEnumerable<SyncJob>> GetByStatusAsync(string status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update sync job.
    /// </summary>
    Task<SyncJob> UpdateAsync(SyncJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete sync job (hard delete, safe for job records).
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if there is at least one SyncJob with the given status for the account.
    /// Used to check for a currently running job before starting a new one.
    /// </summary>
    Task<bool> HasRunningJobAsync(Guid accountId, CancellationToken cancellationToken = default);

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
    /// Save changes to database.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IMonobankCredentialRepository
{
    Task<MonobankCredential> AddAsync(MonobankCredential credential, CancellationToken cancellationToken = default);
    Task<MonobankCredential?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<MonobankCredential?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
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
}

public interface ITrueLayerConnectionRepository
{
    Task<TrueLayerConnection> AddAsync(TrueLayerConnection connection, CancellationToken cancellationToken = default);
    Task<TrueLayerConnection?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TrueLayerConnection?> GetByReferenceAsync(string reference, CancellationToken cancellationToken = default);
    Task<TrueLayerConnection?> GetByUserAndProviderAsync(Guid userId, string providerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TrueLayerConnection>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// LINKED connections whose consent expires on/before <paramref name="threshold"/> (and hasn't
    /// already lapsed) — the pre-expiry reminder detector uses this to nudge the user to reconnect.
    /// </summary>
    Task<IReadOnlyList<TrueLayerConnection>> GetLinkedExpiringBeforeAsync(DateTime threshold, CancellationToken cancellationToken = default);
    Task<TrueLayerConnection> UpdateAsync(TrueLayerConnection connection, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
