namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Core read of the Research coverage universe ("our own index", the capped list of companies the pipeline
/// maintains) so a consumer such as the portfolio-construction screen can read it without depending on the
/// Research module's internals. Same shape as <see cref="IWatchlistReader"/>.
/// </summary>
public interface ICoverageUniverseReader
{
    /// <summary>The user's current coverage members as upper-case tickers, freshest activity first, then by ticker.</summary>
    Task<IReadOnlyList<string>> ListTickersAsync(Guid userId, CancellationToken ct = default);
}
