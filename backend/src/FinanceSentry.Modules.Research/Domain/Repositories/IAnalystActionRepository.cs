namespace FinanceSentry.Modules.Research.Domain.Repositories;

using FinanceSentry.Modules.Research.Domain;

public interface IAnalystActionRepository
{
    /// <summary>
    /// Upserts a batch, deduplicating by logical identity (Ticker + Firm + ActionDate + ActionType).
    /// On conflict the richer record is kept — NULL target/rating fields are filled from the incoming
    /// row; an existing populated field is never overwritten. Returns the number of NEW rows inserted.
    /// </summary>
    Task<int> UpsertAsync(IReadOnlyCollection<AnalystAction> actions, CancellationToken ct = default);

    /// <summary>
    /// Query actions filterable by ticker, cutoff date (inclusive), and action type, newest first.
    /// </summary>
    Task<IReadOnlyList<AnalystAction>> QueryAsync(
        string? ticker,
        DateOnly since,
        AnalystActionType? actionType,
        int limit,
        CancellationToken ct = default);

    /// <summary>
    /// Upgrades and downgrades (not initiations, target changes or reiterations) on <paramref name="tickers"/>
    /// dated on or after <paramref name="since"/>, oldest first. Unbounded by the query limit: the caller
    /// groups a whole window per ticker and day, so a cap would silently drop a firm from a summary.
    /// </summary>
    Task<IReadOnlyList<AnalystAction>> ListRatingChangesAsync(
        IReadOnlyCollection<string> tickers,
        DateOnly since,
        CancellationToken ct = default);

    /// <summary>Resolve a companion event reference back to its exact source-carrying action row.</summary>
    Task<AnalystAction?> GetByIdAsync(Guid id, CancellationToken ct = default);
}
