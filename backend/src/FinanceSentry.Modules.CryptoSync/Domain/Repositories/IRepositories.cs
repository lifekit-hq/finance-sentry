using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;

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

    /// <summary>One venue's open holdings for the user — closed rows (#435 S2) are excluded.</summary>
    Task<IReadOnlyList<CryptoHolding>> GetByUserAndProviderAsync(
        Guid userId, string provider, CancellationToken ct = default);

    /// <summary>One venue's holdings for the user, closed rows included (sync and disconnect only).</summary>
    Task<IReadOnlyList<CryptoHolding>> GetAllByUserAndProviderAsync(
        Guid userId, string provider, CancellationToken ct = default);

    /// <summary>Marks the given tracked holdings for deletion (used to reconcile sold-out assets).</summary>
    void RemoveRange(IEnumerable<CryptoHolding> holdings);

    /// <summary>Deletes one venue's holdings for the user — never another venue's.</summary>
    Task DeleteByUserAndProviderAsync(Guid userId, string provider, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface ICryptoTradeRepository
{
    /// <summary>
    /// Persists fills the walk returned, skipping any already recorded for
    /// <c>(UserId, Provider, TradeId)</c> so a re-walk of an already-covered page adds nothing.
    /// </summary>
    Task AddNewAsync(Guid userId, string provider, IReadOnlyList<CryptoTrade> trades, CancellationToken ct = default);
}
