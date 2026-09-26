namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Logging;

/// <summary>
/// Hangfire job (N1, ledger-heartbeat design): raises a NewsCluster alert when a held name or thesis
/// keyword clusters in the news. Runs every 30 minutes, offset after the news ingestion sweep
/// (<see cref="NewsIngestionJob"/>) so a freshly ingested batch — including per-thesis Google News RSS
/// sources seeded by <see cref="GeopoliticsSourceSeedJob"/>, N2 — is visible to this run. Fires when,
/// within <see cref="ClusterWindow"/>, a ticker's retrievable articles (#693: those with a usable URL —
/// see <see cref="IsRetrievable"/>) — those tagged with the ticker (per-ticker feeds) plus those tagged
/// with any of its theses (registered sources, which carry thesis tags but no ticker tags) — either
/// (a) come from two or more distinct sources reporting two or more distinct stories (#693: a story
/// reprinted verbatim by a second distributor is not independent corroboration — see
/// <see cref="NormalizeTitle"/>), (b) include a hit from a source registered to one of the ticker's
/// theses, or (c) mention a term from <see cref="IMaterialityTermRepository"/>.
/// <see cref="IAlertGeneratorService.GenerateNewsClusterAlertAsync"/> dedups per (ticker, day) and
/// article-level ContentHash dedup at ingestion already collapses re-fetched items, so a feed
/// re-reading the same articles every 30 minutes never re-fires. Designed as the loudest signal
/// (~0.3-1 fires/day); the retrievable-article and distinct-story gates above are #693's fix for that
/// rate drifting several times over target, mostly overnight when wire syndication reposts the same
/// story under multiple distributor names.
/// </summary>
public sealed class NewsMaterialityJob(
    IBankingTotalsReader banking,
    IBrokerageHoldingsReader brokerage,
    IThesisRepository theses,
    INewsRepository news,
    IMaterialityTermRepository materialityTerms,
    IAlertGeneratorService alerts,
    ILogger<NewsMaterialityJob> logger)
{
    private const string EquityInstrumentType = "STK";
    private const int MinClusterSources = 2;
    private const int ArticleLookbackLimit = 50;

    /// <summary>
    /// #693: a cluster backed by fewer than this many articles with a retrievable (non-blank,
    /// well-formed) URL is noise, not a signal — at least one produced alert had zero underlying
    /// articles a reader could actually open.
    /// </summary>
    private const int MinRetrievableArticles = 1;

    private static readonly TimeSpan ClusterWindow = TimeSpan.FromHours(2);

    public Task ExecuteAsync(CancellationToken ct = default) => ExecuteAsync(DateTimeOffset.UtcNow, ct);

    /// <summary>Overload taking the reference instant explicitly, so tests aren't at the mercy of when they run.</summary>
    public async Task ExecuteAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var userIds = await banking.GetActiveUserIdsAsync(ct);
        if (userIds.Count == 0)
        {
            logger.LogDebug("NewsMateriality: no active users found, skipping.");
            return;
        }

        var day = DateOnly.FromDateTime(nowUtc.UtcDateTime);
        var terms = await materialityTerms.ListEnabledTermsAsync(ct);

        foreach (var userId in userIds)
        {
            try
            {
                await ProcessUserAsync(userId, nowUtc, day, terms, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "NewsMateriality: error processing user {UserId}", userId);
            }
        }
    }

    private async Task ProcessUserAsync(
        Guid userId, DateTimeOffset nowUtc, DateOnly day, IReadOnlyList<string> terms, CancellationToken ct)
    {
        var tickerThesisIds = new Dictionary<string, HashSet<Guid>>(StringComparer.OrdinalIgnoreCase);

        foreach (var holding in await brokerage.GetHoldingsAsync(userId, ct))
        {
            if (string.Equals(holding.InstrumentType, EquityInstrumentType, StringComparison.OrdinalIgnoreCase))
            {
                tickerThesisIds.TryAdd(holding.Symbol, []);
            }
        }

        foreach (var thesis in await theses.ListAsync(userId, ct))
        {
            AddThesisTicker(tickerThesisIds, thesis.Ticker, thesis.Id);
            foreach (var trigger in thesis.InvalidationTriggers)
            {
                if (!string.IsNullOrWhiteSpace(trigger.ProxyTicker))
                {
                    tickerThesisIds.TryAdd(trigger.ProxyTicker, []);
                }
            }
        }

        var since = nowUtc - ClusterWindow;
        foreach (var (ticker, thesisIds) in tickerThesisIds)
        {
            await ProcessTickerAsync(userId, ticker, thesisIds, since, day, terms, ct);
        }
    }

    private static void AddThesisTicker(Dictionary<string, HashSet<Guid>> map, string ticker, Guid thesisId)
    {
        if (!map.TryGetValue(ticker, out var thesisIds))
        {
            thesisIds = [];
            map[ticker] = thesisIds;
        }

        thesisIds.Add(thesisId);
    }

    private async Task ProcessTickerAsync(
        Guid userId,
        string ticker,
        HashSet<Guid> thesisIds,
        DateTimeOffset since,
        DateOnly day,
        IReadOnlyList<string> terms,
        CancellationToken ct)
    {
        var articles = (await news.GetForTickerAsync(ticker, since, ArticleLookbackLimit, ct)).ToList();
        foreach (var thesisId in thesisIds)
        {
            articles.AddRange(await news.SearchAsync(null, null, thesisId, since, ArticleLookbackLimit, ct));
        }

        articles = [.. articles.DistinctBy(a => a.ContentHash, StringComparer.Ordinal)];

        // #693: a cluster with no article a reader could actually open is noise, whatever else it
        // qualifies on — filter down to retrievable articles before applying any firing rule.
        var retrievable = articles.Where(IsRetrievable).ToList();
        if (retrievable.Count < MinRetrievableArticles)
        {
            return;
        }

        var distinctSources = retrievable.Select(a => a.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var distinctStories = retrievable.Select(a => NormalizeTitle(a.Title)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (distinctSources >= MinClusterSources && distinctStories >= MinClusterSources)
        {
            await alerts.GenerateNewsClusterAlertAsync(
                userId, ticker, $"{distinctSources} sources within {ClusterWindow.TotalHours:0}h", day, ct);
            return;
        }

        var thesisHit = thesisIds.Count > 0 && retrievable.Any(a => a.ThesisIds.Any(thesisIds.Contains));
        if (thesisHit)
        {
            await alerts.GenerateNewsClusterAlertAsync(userId, ticker, "thesis-attached source hit", day, ct);
            return;
        }

        var matchedKeyword = retrievable
            .SelectMany(a => terms.Where(k => Mentions(a, k)))
            .FirstOrDefault();
        if (matchedKeyword is not null)
        {
            await alerts.GenerateNewsClusterAlertAsync(userId, ticker, $"material keyword: {matchedKeyword}", day, ct);
        }
    }

    /// <summary>An article with no usable link is not a retrievable underlying source (#693).</summary>
    private static bool IsRetrievable(NewsArticle article)
        => !string.IsNullOrWhiteSpace(article.Url)
            && Uri.IsWellFormedUriString(article.Url, UriKind.Absolute);

    /// <summary>
    /// Collapses a headline to whitespace/case-insensitive text so the same story reprinted verbatim
    /// by a second wire distributor doesn't count as a second, independent story (#693).
    /// </summary>
    private static string NormalizeTitle(string title)
        => string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim()
            .TrimEnd('.', '!', '?');

    private static bool Mentions(NewsArticle article, string keyword)
        => article.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || (article.Summary?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false);
}
