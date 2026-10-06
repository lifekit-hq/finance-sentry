namespace FinanceSentry.Modules.Research.Infrastructure.Sources;

using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using FinanceSentry.Modules.Research.Application.Services;
using Microsoft.Extensions.Logging;

/// <summary>
/// <c>Page</c>-kind news source for the TrendForce press center (feature 030, R2) — the DRAM/memory
/// thesis feed Denys's channels cite that has no RSS. Scrapes the article list with AngleSharp;
/// structural assertions throw <see cref="NewsSourceParseException"/> on markup drift (FR-009).
/// </summary>
public sealed class TrendForcePageSource(
    IHttpClientFactory httpFactory,
    ILogger<TrendForcePageSource> logger) : INewsPageSource
{
    public const string HttpClientName = "trendforce";

    private const string Host = "trendforce.com";

    // Known card container shapes, most-specific first. TrendForce has restyled the press list twice
    // (list-item -> niche-box-post), so class selectors are treated as a fast path only: when none
    // match, ParseAsync falls back to the stable article-href pattern below (FindArticleNodesByHref),
    // which survives CSS churn.
    private static readonly string[] ArticleSelectors =
    [
        ".niche-box-post",
        ".press-news-list .list-items > .list-item",
        ".list-items > .list-item",
        "article",
        ".press-item"
    ];

    // TrendForce article permalinks: /presscenter/news/<8-digit-date>-<id>.html. Category/index links
    // (e.g. /presscenter/news/Semiconductors) do not match, so this cleanly isolates real articles.
    // It gates BOTH discovery paths (issue #318): a card's own anchors can point at /presscenter/chart/,
    // /presscenter/video/ or off-site promos, which were being ingested as thesis news.
    private static readonly Regex ArticleHrefPattern =
        new(@"/presscenter/news/\d{6,}-\d+\.html$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public bool CanHandle(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Host.Contains(Host, StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<NewsPageArticle>> FetchAsync(string url, CancellationToken ct = default)
    {
        var client = httpFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(ct);

        var articles = await ParseAsync(html, url, ct);
        logger.LogInformation("TrendForce press center parsed {Count} articles", articles.Count);
        return articles;
    }

    /// <summary>
    /// Parses the press-center article list out of a TrendForce HTML page. Public + static so the
    /// contract test can drive it against a recorded fixture with no HTTP. Throws when no article
    /// nodes are present (markup drift).
    /// </summary>
    public static async Task<IReadOnlyList<NewsPageArticle>> ParseAsync(
        string html, string pageUrl, CancellationToken ct = default)
    {
        var parser = new HtmlParser();
        using var doc = await parser.ParseDocumentAsync(html, ct);

        var nodes = FindArticleNodes(doc);
        if (nodes.Count == 0)
        {
            throw new NewsSourceParseException(
                "TrendForce press-center article list not found — page markup may have changed.");
        }

        var baseUri = Uri.TryCreate(pageUrl, UriKind.Absolute, out var b) ? b : null;
        var articles = new List<NewsPageArticle>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in nodes)
        {
            var anchor = FindArticleAnchor(node);
            if (anchor is null)
            {
                continue;
            }

            var href = anchor.GetAttribute("href")?.Trim();
            var title = ExtractTitle(node, anchor);
            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var absoluteUrl = Resolve(baseUri, href);
            if (!seen.Add(absoluteUrl))
            {
                continue;
            }

            articles.Add(new NewsPageArticle(
                title!.Trim(),
                absoluteUrl,
                ExtractDate(node),
                ExtractSummary(node)));
        }

        if (articles.Count == 0)
        {
            throw new NewsSourceParseException(
                "TrendForce press-center cards carried no article permalinks — page markup may have changed.");
        }

        return articles;
    }

    /// <summary>
    /// The card's press-release anchor, or null when it has none. Selecting by permalink rather than by
    /// position keeps promo cards (charts, videos, off-site links) out of the feed and makes both
    /// discovery paths agree on what counts as an article.
    /// </summary>
    private static IElement? FindArticleAnchor(IElement node) =>
        node.QuerySelectorAll("a[href]")
            .FirstOrDefault(a => ArticleHrefPattern.IsMatch(a.GetAttribute("href") ?? string.Empty));

    private static IReadOnlyList<IElement> FindArticleNodes(IDocument doc)
    {
        foreach (var selector in ArticleSelectors)
        {
            var nodes = doc.QuerySelectorAll(selector)
                .Where(n => n.QuerySelector("a[href]") is not null)
                .ToList();
            if (nodes.Count > 0)
            {
                return nodes;
            }
        }

        // Fallback: recover article cards from the stable permalink pattern when the card classes have
        // drifted. Each matching anchor is climbed to its nearest card ancestor; duplicates (multiple
        // anchors per card, e.g. image + headline) collapse via reference equality.
        var byHref = doc.QuerySelectorAll("a[href]")
            .Where(a => ArticleHrefPattern.IsMatch(a.GetAttribute("href") ?? string.Empty))
            .Select(FindCardByAnchor)
            .Distinct()
            .ToList();

        return byHref;
    }

    /// <summary>
    /// Climbs from an article anchor to the smallest ancestor that looks like a self-contained card —
    /// one carrying a heading and at least one sibling element (date/summary). Falls back to the direct
    /// parent so extraction still has a scope to query.
    /// </summary>
    private static IElement FindCardByAnchor(IElement anchor)
    {
        const int MaxDepth = 5;
        var current = anchor;
        for (var depth = 0; depth < MaxDepth; depth++)
        {
            var parent = current.ParentElement;
            if (parent is null)
            {
                break;
            }

            var hasHeading = parent.QuerySelector("h1, h2, h3, h4") is not null;
            if (hasHeading && parent.Children.Length >= 2)
            {
                return parent;
            }

            current = parent;
        }

        return anchor.ParentElement ?? anchor;
    }

    private static string? ExtractTitle(IElement node, IElement anchor)
    {
        var heading = node.QuerySelector("h1, h2, h3, .title, a.title-link");
        var text = heading?.TextContent ?? anchor.TextContent;
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static DateTimeOffset ExtractDate(IElement node)
    {
        var timeEl = node.QuerySelector("time[datetime]");
        var raw = timeEl?.GetAttribute("datetime")
            ?? node.QuerySelector("time, .date, .time, h4.color-green, .bd-month, .block-data")?.TextContent;

        // A date with no offset is UTC, never the host's local zone; one with an offset keeps its instant.
        return DateTimeOffset.TryParse(
            raw?.Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;
    }

    private static string? ExtractSummary(IElement node)
    {
        // Prefer an explicit summary element; otherwise the first paragraph that isn't the date block
        // (TrendForce renders the date inside <p class="bd-day/bd-month">, which must not become the body).
        foreach (var el in node.QuerySelectorAll(".summary, .desc, p"))
        {
            if (el.ClassList.Contains("bd-day") || el.ClassList.Contains("bd-month"))
            {
                continue;
            }

            var text = el.TextContent?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static string Resolve(Uri? baseUri, string href)
    {
        var isAbsolute = Uri.TryCreate(href, UriKind.Absolute, out var absolute);
        if (isAbsolute && absolute!.Scheme is "http" or "https")
        {
            return absolute.ToString();
        }

        // AngleSharp rewrites root-relative hrefs to file:// when parsed without a base — recover the
        // original path and resolve it against the real page URL.
        var relative = isAbsolute && absolute!.Scheme == "file" ? absolute.PathAndQuery : href;
        return baseUri is not null && Uri.TryCreate(baseUri, relative, out var combined)
            ? combined.ToString()
            : href;
    }
}
