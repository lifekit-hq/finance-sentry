namespace FinanceSentry.Modules.CryptoSync.Domain.Repositories;

public interface IExchangeCredentialRepository
{
    Task AddAsync(ExchangeCredential credential, CancellationToken ct = default);
    Task<ExchangeCredential?> GetAsync(Guid userId, string provider, CancellationToken ct = default);
    Task<IReadOnlyList<ExchangeCredential>> GetAllActiveAsync(string provider, CancellationToken ct = default);
    void Update(ExchangeCredential credential);
    void Delete(ExchangeCredential credential);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface ICryptoHoldingRepository
{
    /// <summary>Upserts on <c>(UserId, Provider, Asset)</c>.</summary>
    Task UpsertRangeAsync(IReadOnlyList<CryptoHolding> holdings, CancellationToken ct = default);

    /// <summary>Every venue's holdings for the user.</summary>
    Task<IReadOnlyList<CryptoHolding>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>One venue's holdings for the user.</summary>
    Task<IReadOnlyList<CryptoHolding>> GetByUserAndProviderAsync(
        Guid userId, string provider, CancellationToken ct = default);

    /// <summary>Marks the given tracked holdings for deletion (used to reconcile sold-out assets).</summary>
    void RemoveRange(IEnumerable<CryptoHolding> holdings);

    /// <summary>Deletes one venue's holdings for the user — never another venue's.</summary>
    Task DeleteByUserAndProviderAsync(Guid userId, string provider, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
