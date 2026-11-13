namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
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
/// <para>
/// A cluster on a ticker that maps to a thesis (its own ticker or a trigger proxy) or to an Active candidate also raises the
/// thesis-review notice (<see cref="ReviewDueRaiser"/>, source <c>news</c>, reference = ticker and day, evidence = the
/// article ids). Active-candidate tickers outside the held and thesis names are scanned for that notice alone: a candidate
/// is not a holding, so it earns no NewsCluster alert of its own.
/// </para>
/// <para>
/// Coverage names (<see cref="LookaheadUniverse.CoverageTickersByPriorityAsync"/>) are scanned the same way, for the notice and
/// the coverage event review alone (<see cref="ReviewDueRaiser.CompleteAsync"/>): a cluster on a name we only cover moves its
/// <c>LastEventAt</c>, re-scores its candidate and runs the coverage remove rules, with no NewsCluster alert.
/// </para>
/// </summary>
public sealed class NewsMaterialityJob(
    IBankingTotalsReader banking,
    IBrokerageHoldingsReader brokerage,
    LookaheadUniverse universe,
    IThesisRepository theses,
    INewsRepository news,
    IMaterialityTermRepository materialityTerms,
    IAlertGeneratorService alerts,
    ReviewDueRaiser reviewDue,
    ILogger<NewsMaterialityJob> logger,
    int minGoogleNewsThesisStories = NewsMaterialityJob.DefaultMinGoogleNewsThesisStories)
{
    /// <summary>
    /// Distinct fresh stories a Google-News-seeded thesis source must produce inside
    /// <see cref="ClusterWindow"/> to fire the thesis-attached rule. One article fired it ~3.3 times a
    /// day against a ~0.2/day design; replaying the real history put N=4 at ~0.34/day. Other thesis
    /// sources keep a threshold of one.
    /// </summary>
    public const int DefaultMinGoogleNewsThesisStories = 4;

    private const string EquityInstrumentType = "STK";
    private const int MinClusterSources = 2;
    private const int ArticleLookbackLimit = 50;

    /// <summary>How many article ids the review notice names; the newest first.</summary>
    private const int MaxEvidenceArticles = 5;

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

        foreach (var thesis in await theses.ListUnscopedAsync(userId, ct))
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

        var subjects = await reviewDue.LoadSubjectsAsync(userId, ct);
        subjects.TrackCoverage(await universe.CoverageTickersByPriorityAsync(userId, ct));
        var reviewOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ticker in subjects.WatchedTickers)
        {
            if (tickerThesisIds.TryAdd(ticker, []))
            {
                reviewOnly.Add(ticker);
            }
        }

        var since = nowUtc - ClusterWindow;
        foreach (var (ticker, thesisIds) in tickerThesisIds)
        {
            await ProcessTickerAsync(
                userId, ticker, thesisIds, since, day, terms, subjects, raiseClusterAlert: !reviewOnly.Contains(ticker), ct);
        }

        await reviewDue.CompleteAsync(userId, subjects, ct);
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
        ReviewSubjects subjects,
        bool raiseClusterAlert,
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
            await RaiseAsync($"{distinctSources} sources within {ClusterWindow.TotalHours:0}h");
            return;
        }

        var thesisArticles = thesisIds.Count == 0
            ? []
            : retrievable.Where(a => a.ThesisIds.Any(thesisIds.Contains)).ToList();
        if (IsThesisHit(thesisArticles))
        {
            await RaiseAsync("thesis-attached source hit");
            return;
        }

        var matchedKeyword = retrievable
            .SelectMany(a => terms.Where(k => Mentions(a, k)))
            .FirstOrDefault();
        if (matchedKeyword is not null)
        {
            await RaiseAsync($"material keyword: {matchedKeyword}");
        }

        async Task RaiseAsync(string reason)
        {
            if (raiseClusterAlert)
            {
                await alerts.GenerateNewsClusterAlertAsync(userId, ticker, reason, day, ct);
            }

            var evidence = retrievable
                .OrderByDescending(a => a.PublishedAt)
                .Take(MaxEvidenceArticles)
                .Select(a => $"article:{a.Id}")
                .ToList();
            await reviewDue.RaiseAsync(
                userId, subjects, ticker, ReviewDueSource.News, $"{ticker.ToUpperInvariant()}:{day:yyyy-MM-dd}", evidence,
                $"news clustered on {day:yyyy-MM-dd} ({reason})", ct);
        }
    }

    /// <summary>
    /// A hit from a curated thesis source fires on its own; hits that only come from Google-News-seeded
    /// sources (one source name per thesis, publisher lost) need
    /// <c>minGoogleNewsThesisStories</c> distinct stories.
    /// </summary>
    private bool IsThesisHit(List<NewsArticle> thesisArticles)
        => thesisArticles.Any(a => !IsGoogleNewsSeeded(a))
            || (thesisArticles.Count > 0
                && thesisArticles.Select(a => NormalizeTitle(a.Title)).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                    >= minGoogleNewsThesisStories);

    private static bool IsGoogleNewsSeeded(NewsArticle article)
        => article.Source.StartsWith($"src:{GeopoliticsSourceSeedJob.SourceNamePrefix}", StringComparison.OrdinalIgnoreCase);

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
