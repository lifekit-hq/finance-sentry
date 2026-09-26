namespace FinanceSentry.Modules.BrokerageSync.Domain.Repositories;

public interface IIBKRCredentialRepository
{
    Task AddAsync(IBKRCredential credential, CancellationToken ct = default);
    Task<IBKRCredential?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IBKRCredential?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<IBKRCredential>> GetAllActiveAsync(CancellationToken ct = default);
    void Update(IBKRCredential credential);
    void Delete(IBKRCredential credential);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IIBKRFlexCredentialRepository
{
    Task AddAsync(IBKRFlexCredential credential, CancellationToken ct = default);
    Task<IBKRFlexCredential?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<IBKRFlexCredential>> GetAllActiveAsync(CancellationToken ct = default);
    void Update(IBKRFlexCredential credential);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IBrokerageHoldingRepository
{
    Task UpsertRangeAsync(IEnumerable<BrokerageHolding> holdings, CancellationToken ct = default);
    Task<IReadOnlyList<BrokerageHolding>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Marks the given tracked holdings for deletion (used to reconcile sold-out positions).</summary>
    void RemoveRange(IEnumerable<BrokerageHolding> holdings);

    Task DeleteByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IBrokerageInstrumentRepository
{
    Task<BrokerageInstrument?> GetByConidAsync(Guid userId, string provider, long conid, CancellationToken ct = default);

    /// <summary>Scoped to <paramref name="userId"/> so a caller can never resolve — or change — another user's instrument.</summary>
    Task<BrokerageInstrument?> GetByIdAsync(Guid userId, Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<BrokerageInstrument>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(BrokerageInstrument instrument, CancellationToken ct = default);
    void Update(BrokerageInstrument instrument);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IBrokerageTradeRepository
{
    Task<BrokerageTrade?> GetByExecutionIdAsync(Guid userId, string provider, string ibExecutionId, CancellationToken ct = default);
    Task AddAsync(BrokerageTrade trade, CancellationToken ct = default);
    void Update(BrokerageTrade trade);
    Task<IReadOnlyList<BrokerageTrade>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IBrokerageCashTransactionRepository
{
    Task<BrokerageCashTransaction?> GetByIdempotencyKeyAsync(Guid userId, string provider, string idempotencyKey, CancellationToken ct = default);
    Task AddAsync(BrokerageCashTransaction transaction, CancellationToken ct = default);
    Task<IReadOnlyList<BrokerageCashTransaction>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
