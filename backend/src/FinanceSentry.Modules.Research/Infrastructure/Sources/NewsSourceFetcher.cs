namespace FinanceSentry.Modules.Research.Infrastructure.Sources;

using System.Security.Cryptography;
using System.Text;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;

/// <summary>
/// Fetches one registered <see cref="NewsSource"/> and maps it to persistable articles: RSS feeds go
/// through <see cref="IMarketNewsService"/>, Page sources through the <see cref="INewsPageSource"/>
/// that claims the URL (feature 030, FR-007/FR-008). Articles come back stamped with the source name,
/// its thesis tags and a content hash.
/// <para>
/// It is shared rather than private to the ingestion job because a source is fetched from two places:
/// the 30-minute ingestion sweep over enabled sources, and the recovery probe that tries retired ones
/// (spec 047). Both must agree on what fetching a source means — a probe that skipped tagging or
/// hashing would revive a source into a different pipeline than the one that retired it.
/// </para>
/// </summary>
public sealed class NewsSourceFetcher(IMarketNewsService news, IEnumerable<INewsPageSource> pageSources)
{
    private const int MaxTitleLength = 500;
    private const int MaxUrlLength = 2000;
    private const int MaxSummaryLength = 4000;

    /// <summary>
    /// Fetches the source's current articles. Throws on an unreachable feed/page, on markup drift, or
    /// when no page source handles the URL — the caller decides what a failure means for the source's
    /// health.
    /// </summary>
    public async Task<IReadOnlyList<NewsArticle>> FetchAsync(NewsSource source, CancellationToken ct = default)
    {
        var articles = source.Kind == NewsSourceKind.Rss
            ? await FetchRssAsync(source, ct)
            : await FetchPageAsync(source, ct);

        foreach (var article in articles)
        {
            article.ThesisIds = NewsSourceTagging.ResolveThesisIds(source, article.Title, article.Summary).ToList();
        }

        return articles;
    }

    private async Task<IReadOnlyList<NewsArticle>> FetchRssAsync(NewsSource source, CancellationToken ct)
        => await news.FetchFeedArticlesAsync(source.Url, $"src:{source.Name}", ct);

    private async Task<IReadOnlyList<NewsArticle>> FetchPageAsync(NewsSource source, CancellationToken ct)
    {
        var pageSource = pageSources.FirstOrDefault(p => p.CanHandle(source.Url))
            ?? throw new NewsSourceParseException(
                $"No page source registered to handle '{source.Url}'.");

        var candidates = await pageSource.FetchAsync(source.Url, ct);
        return candidates
            .Select(c => new NewsArticle
            {
                Source = $"src:{source.Name}",
                Title = Trim(c.Title, MaxTitleLength),
                Url = Trim(c.Url, MaxUrlLength),
                Summary = c.Summary is null ? null : Trim(c.Summary, MaxSummaryLength),
                PublishedAt = c.PublishedAt,
                ContentHash = HashContent(c.Url, c.Title),
            })
            .ToList();
    }

    private static string HashContent(string url, string title)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{url}\n{title}")));

    private static string Trim(string value, int max)
        => value.Length <= max ? value : value[..max];
}
