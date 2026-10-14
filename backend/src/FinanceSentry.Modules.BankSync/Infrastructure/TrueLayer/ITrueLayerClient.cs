namespace FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;

public interface ITrueLayerClient
{
    Task<IReadOnlyList<TrueLayerProvider>> ListProvidersAsync(string? country, CancellationToken ct = default);

    string BuildAuthLink(string providerId, string reference, string redirectUri);

    Task<TrueLayerTokenSet> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default);

    Task<TrueLayerTokenSet> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>Withdraws the connection's access at TrueLayer (<c>DELETE /api/delete</c>), so its tokens stop working.</summary>
    Task RevokeAccessAsync(string accessToken, CancellationToken ct = default);

    Task<IReadOnlyList<TrueLayerAccountInfo>> ListAccountsAsync(string accessToken, CancellationToken ct = default);

    Task<TrueLayerAccountBalance?> GetBalanceAsync(string accessToken, string accountId, CancellationToken ct = default);

    Task<IReadOnlyList<TrueLayerTransaction>> GetTransactionsAsync(
        string accessToken,
        string accountId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken ct = default);

    Task<IReadOnlyList<TrueLayerTransaction>> GetPendingTransactionsAsync(
        string accessToken,
        string accountId,
        CancellationToken ct = default);

    // ── Cards ─────────────────────────────────────────────────────────────────
    // TrueLayer serves credit cards under /data/v1/cards, NOT /data/v1/accounts —
    // an accounts-only integration never sees them at all.

    Task<IReadOnlyList<TrueLayerAccountInfo>> ListCardsAsync(string accessToken, CancellationToken ct = default);

    Task<TrueLayerCardBalance?> GetCardBalanceAsync(string accessToken, string cardId, CancellationToken ct = default);

    Task<IReadOnlyList<TrueLayerTransaction>> GetCardTransactionsAsync(
        string accessToken,
        string cardId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken ct = default);

    Task<IReadOnlyList<TrueLayerTransaction>> GetCardPendingTransactionsAsync(
        string accessToken,
        string cardId,
        CancellationToken ct = default);
}
