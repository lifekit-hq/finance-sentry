namespace FinanceSentry.Modules.Research.Domain.Repositories;

public interface INewsRepository
{
    Task<IReadOnlyList<NewsArticle>> SearchAsync(
        string? query,
        IReadOnlyCollection<string>? tickers,
        Guid? thesisId,
        DateTimeOffset? since,
        int limit,
        CancellationToken ct = default);

    Task<IReadOnlyList<NewsArticle>> GetForTickerAsync(
        string ticker, DateTimeOffset? since, int limit, CancellationToken ct = default);

    /// <summary>Articles tagged <paramref name="ticker"/> published in [<paramref name="from"/>, <paramref name="until"/>), newest first, capped at <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<NewsArticle>> GetForTickerBetweenAsync(
        string ticker, DateTimeOffset from, DateTimeOffset until, int limit, CancellationToken ct = default);

    Task<int> InsertNewAsync(IReadOnlyCollection<NewsArticle> articles, CancellationToken ct = default);
}
