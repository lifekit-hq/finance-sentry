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
/// within <see cref="ClusterWindow"/>, a ticker's articles — those tagged with the ticker (per-ticker
/// feeds) plus those tagged with any of its theses (registered sources, which carry thesis tags but no
/// ticker tags) — either (a) come from two or more distinct
/// sources, (b) include a hit from a source registered to one of the ticker's theses, or (c) mention a
/// <see cref="MaterialKeywords"/> term. <see cref="IAlertGeneratorService.GenerateNewsClusterAlertAsync"/>
/// dedups per (ticker, day) and article-level ContentHash dedup at ingestion already collapses
/// re-fetched items, so a feed re-reading the same articles every 30 minutes never re-fires. The
/// loudest signal in the design (~0.3-1 fires/day) — noise controls are the feature.
/// </summary>
public sealed class NewsMaterialityJob(
    IBankingTotalsReader banking,
    IBrokerageHoldingsReader brokerage,
    IThesisRepository theses,
    INewsRepository news,
    IAlertGeneratorService alerts,
    ILogger<NewsMaterialityJob> logger)
{
    private const string EquityInstrumentType = "STK";
    private const int MinClusterSources = 2;
    private const int ArticleLookbackLimit = 50;

    private static readonly TimeSpan ClusterWindow = TimeSpan.FromHours(2);

    /// <summary>
    /// The configured material class (report §5.3, N1): a hit on any of these terms fires regardless
    /// of source count. Deliberately small — breadth belongs to the multi-source clustering rule, not
    /// this list.
    /// </summary>
    private static readonly string[] MaterialKeywords =
        ["guidance", "downgrade", "investigation", "M&A", "halted", "recall", "acquisition"];

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

        foreach (var userId in userIds)
        {
            try
            {
                await ProcessUserAsync(userId, nowUtc, day, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "NewsMateriality: error processing user {UserId}", userId);
            }
        }
    }

    private async Task ProcessUserAsync(Guid userId, DateTimeOffset nowUtc, DateOnly day, CancellationToken ct)
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
                    AddThesisTicker(tickerThesisIds, trigger.ProxyTicker, thesis.Id);
                }
            }
        }

        var since = nowUtc - ClusterWindow;
        foreach (var (ticker, thesisIds) in tickerThesisIds)
        {
            await ProcessTickerAsync(userId, ticker, thesisIds, since, day, ct);
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
        Guid userId, string ticker, HashSet<Guid> thesisIds, DateTimeOffset since, DateOnly day, CancellationToken ct)
    {
        var articles = (await news.GetForTickerAsync(ticker, since, ArticleLookbackLimit, ct)).ToList();
        foreach (var thesisId in thesisIds)
        {
            articles.AddRange(await news.SearchAsync(null, null, thesisId, since, ArticleLookbackLimit, ct));
        }

        articles = [.. articles.DistinctBy(a => a.ContentHash, StringComparer.Ordinal)];
        if (articles.Count == 0)
        {
            return;
        }

        var distinctSources = articles.Select(a => a.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (distinctSources >= MinClusterSources)
        {
            await alerts.GenerateNewsClusterAlertAsync(
                userId, ticker, $"{distinctSources} sources within {ClusterWindow.TotalHours:0}h", day, ct);
            return;
        }

        var thesisHit = thesisIds.Count > 0 && articles.Any(a => a.ThesisIds.Any(thesisIds.Contains));
        if (thesisHit)
        {
            await alerts.GenerateNewsClusterAlertAsync(userId, ticker, "thesis-attached source hit", day, ct);
            return;
        }

        var matchedKeyword = articles
            .SelectMany(a => MaterialKeywords.Where(k => Mentions(a, k)))
            .FirstOrDefault();
        if (matchedKeyword is not null)
        {
            await alerts.GenerateNewsClusterAlertAsync(userId, ticker, $"material keyword: {matchedKeyword}", day, ct);
        }
    }

    private static bool Mentions(NewsArticle article, string keyword)
        => article.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || (article.Summary?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false);
}
