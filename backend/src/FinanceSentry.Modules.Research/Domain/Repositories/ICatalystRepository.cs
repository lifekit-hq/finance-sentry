namespace FinanceSentry.Modules.Research.Domain.Repositories;

using FinanceSentry.Modules.Research.Domain.Catalyst;

/// <summary>
/// Store of the catalyst events the daily 8-K scan recorded. The scan runs with no person in scope, so every read here opts out of
/// the Owner query filter and keeps its own <c>UserId</c> predicate.
/// </summary>
public interface ICatalystRepository
{
    /// <summary>
    /// The filings the user has already handled, as <c>TICKER|ACCESSION</c> keys, dated on or after <paramref name="since"/>.
    /// Opts out of the Owner query filter.
    /// </summary>
    Task<IReadOnlySet<string>> ListHandledUnscopedAsync(Guid userId, DateOnly since, CancellationToken ct = default);

    /// <summary>
    /// Records events whose (ticker, accession) the user has not recorded yet; a repeat is dropped. Returns the rows inserted.
    /// Opts out of the Owner query filter.
    /// </summary>
    Task<int> AddEventsUnscopedAsync(Guid userId, IReadOnlyCollection<CatalystEvent> events, CancellationToken ct = default);

    /// <summary>
    /// The user's catalyst events detected at or after <paramref name="since"/>, newest first, at most <paramref name="take"/>,
    /// with the count of every match. Opts out of the Owner query filter.
    /// </summary>
    Task<(IReadOnlyList<CatalystEvent> Events, int Total)> ListEventsUnscopedAsync(
        Guid userId, DateTimeOffset since, int take, CancellationToken ct = default);

    /// <summary>Deletes catalyst events detected before <paramref name="before"/> for every user. Opts out of the Owner query filter.</summary>
    Task<int> PruneEventsUnscopedAsync(DateTimeOffset before, CancellationToken ct = default);
}

/// <summary>Store of the GDELT article-volume readings per theme phrase (shared market data, no user).</summary>
public interface IThemeAttentionRepository
{
    /// <summary>
    /// Writes the days read for one theme phrase, replacing a stored day of the same (theme, phrase, day) and adding the rest.
    /// Returns the number of days written.
    /// </summary>
    Task<int> UpsertDaysAsync(IReadOnlyCollection<ThemeAttentionDay> days, CancellationToken ct = default);

    /// <summary>Stored days on or after <paramref name="since"/>, optionally for one theme list key, ordered by theme, phrase, day.</summary>
    Task<IReadOnlyList<ThemeAttentionDay>> ListDaysAsync(string? theme, DateOnly since, CancellationToken ct = default);

    /// <summary>Deletes days before <paramref name="before"/>.</summary>
    Task<int> PruneAsync(DateOnly before, CancellationToken ct = default);
}
