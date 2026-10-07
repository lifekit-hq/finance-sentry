using FinanceSentry.Core.Connections;

namespace FinanceSentry.Modules.BrokerageSync.Domain.Repositories;

public interface IIBKRCredentialRepository
{
    Task AddAsync(IBKRCredential credential, CancellationToken ct = default);

    /// <summary>Credential by ID for the OAuth adapter, which has no person in scope. Opts out of the Owner query filter.</summary>
    Task<IBKRCredential?> GetByIdUnscopedAsync(Guid id, CancellationToken ct = default);

    Task<IBKRCredential?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The user's credential for the scheduled sync, which has no person in scope. Opts out of the Owner query filter.</summary>
    Task<IBKRCredential?> GetByUserIdUnscopedAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Every user's active credentials, for the sync sweep. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<IBKRCredential>> GetAllActiveUnscopedAsync(CancellationToken ct = default);

    /// <summary>
    /// Stores a credential's connection health alone, straight to the row: the shadow recorder runs after the sync's own
    /// save, whatever state that left the change tracker in. For the no-person sync; opts out of the Owner query filter.
    /// </summary>
    Task SaveHealthUnscopedAsync(Guid credentialId, ConnectionHealth health, CancellationToken ct = default);

    void Update(IBKRCredential credential);
    void Delete(IBKRCredential credential);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IIBKRFlexCredentialRepository
{
    Task AddAsync(IBKRFlexCredential credential, CancellationToken ct = default);
    Task<IBKRFlexCredential?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The user's credential for the scheduled sync, which has no person in scope. Opts out of the Owner query filter.</summary>
    Task<IBKRFlexCredential?> GetByUserIdUnscopedAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Every user's active credentials, for the sync sweep. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<IBKRFlexCredential>> GetAllActiveUnscopedAsync(CancellationToken ct = default);
    void Update(IBKRFlexCredential credential);
    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Writes the credential's <see cref="IBKRFlexCredential.LastError"/> straight to the row, bypassing the
    /// change tracker, so a sync job can record a persist-stage failure on the same context whose
    /// <c>SaveChanges</c> just threw, then discards everything still tracked so the failed unit of work
    /// cannot leak into the next user or window of the same sweep. The sweep has no person in scope.
    /// Opts out of the Owner query filter.
    /// </summary>
    Task SaveLastErrorUnscopedAsync(IBKRFlexCredential credential, CancellationToken ct = default);

    /// <summary>
    /// Stores a credential's connection health alone, straight to the row: the shadow recorder runs after the sync's own
    /// save, whatever state that left the change tracker in. For the no-person sync; opts out of the Owner query filter.
    /// </summary>
    Task SaveHealthUnscopedAsync(Guid credentialId, ConnectionHealth health, CancellationToken ct = default);
}

public interface IInzhurCredentialRepository
{
    Task AddAsync(InzhurCredential credential, CancellationToken ct = default);
    Task<InzhurCredential?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The user's credential for the no-person sync. Opts out of the Owner query filter.</summary>
    Task<InzhurCredential?> GetByUserIdUnscopedAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Every user's credential holding a live session, for the daily sweep. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<InzhurCredential>> GetAllActiveUnscopedAsync(CancellationToken ct = default);

    /// <summary>
    /// Stores a credential's connection health alone, straight to the row: the shadow recorder runs after the sync's own
    /// save, whatever state that left the change tracker in. For the no-person sync; opts out of the Owner query filter.
    /// </summary>
    Task SaveHealthUnscopedAsync(Guid credentialId, ConnectionHealth health, CancellationToken ct = default);

    void Update(InzhurCredential credential);
    void Delete(InzhurCredential credential);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IBrokerageHoldingRepository
{
    Task UpsertRangeAsync(IEnumerable<BrokerageHolding> holdings, CancellationToken ct = default);
    Task<IReadOnlyList<BrokerageHolding>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The user's holdings for the sync and the cross-module reader, which have no person in scope. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<BrokerageHolding>> GetByUserIdUnscopedAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Marks the given tracked holdings for deletion (used to reconcile sold-out positions).</summary>
    void RemoveRange(IEnumerable<BrokerageHolding> holdings);

    /// <summary>Deletes one provider's holdings for the user — a disconnect never touches another provider's rows.</summary>
    Task DeleteByUserIdAndProviderAsync(Guid userId, string provider, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IBrokerageInstrumentRepository
{
    /// <summary>Sync dedup read: the scheduled sync has no person in scope. Opts out of the Owner query filter.</summary>
    Task<BrokerageInstrument?> GetByConidUnscopedAsync(Guid userId, string provider, long conid, CancellationToken ct = default);

    /// <summary>Scoped to <paramref name="userId"/> so a caller can never resolve — or change — another user's instrument.</summary>
    Task<BrokerageInstrument?> GetByIdAsync(Guid userId, Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<BrokerageInstrument>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(BrokerageInstrument instrument, CancellationToken ct = default);
    void Update(BrokerageInstrument instrument);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IBrokerageTradeRepository
{
    /// <summary>Sync dedup read: the scheduled sync has no person in scope. Opts out of the Owner query filter.</summary>
    Task<BrokerageTrade?> GetByExecutionIdUnscopedAsync(Guid userId, string provider, string ibExecutionId, CancellationToken ct = default);
    Task AddAsync(BrokerageTrade trade, CancellationToken ct = default);
    void Update(BrokerageTrade trade);
    Task<IReadOnlyList<BrokerageTrade>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The user's trades for the cross-module reader, which has no person in scope. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<BrokerageTrade>> GetByUserIdUnscopedAsync(Guid userId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IBrokerageCashTransactionRepository
{
    /// <summary>Sync dedup read: the scheduled sync has no person in scope. Opts out of the Owner query filter.</summary>
    Task<BrokerageCashTransaction?> GetByIdempotencyKeyUnscopedAsync(Guid userId, string provider, string idempotencyKey, CancellationToken ct = default);
    Task AddAsync(BrokerageCashTransaction transaction, CancellationToken ct = default);
    Task<IReadOnlyList<BrokerageCashTransaction>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
