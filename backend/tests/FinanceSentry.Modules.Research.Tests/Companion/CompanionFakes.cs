namespace FinanceSentry.Modules.Research.Tests.Companion;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>Shared test doubles for the companion-layer (feature 030) unit tests.</summary>
internal sealed class FakeAnalystUniverseRepository : IAnalystUniverseRepository
{
    public List<AnalystUniverseMember> Members { get; } = [];

    public List<string> Deactivated { get; } = [];

    public Task<IReadOnlyList<AnalystUniverseMember>> ListActiveAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AnalystUniverseMember>>(Members.Where(m => m.Active).ToList());

    public Task<IReadOnlyList<AnalystUniverseMember>> ListAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AnalystUniverseMember>>(Members.ToList());

    public Task UpsertMembersAsync(IReadOnlyCollection<AnalystUniverseMember> members, CancellationToken ct = default)
    {
        foreach (var member in members)
        {
            var existing = Members.FirstOrDefault(m => m.Ticker == member.Ticker);
            if (existing is null)
            {
                Members.Add(member);
            }
            else
            {
                existing.Active = true;
                existing.Reason = member.Reason;
            }
        }

        return Task.CompletedTask;
    }

    public Task DeactivateAsync(IReadOnlyCollection<string> tickers, CancellationToken ct = default)
    {
        foreach (var ticker in tickers)
        {
            Deactivated.Add(ticker);
            var member = Members.FirstOrDefault(m => m.Ticker == ticker);
            if (member is not null)
            {
                member.Active = false;
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> IsInUniverseAsync(string ticker, CancellationToken ct = default)
    {
        var upper = ticker.Trim().ToUpperInvariant();
        return Task.FromResult(Members.Any(m => m.Ticker == upper && m.Active));
    }
}

internal sealed class FakeAnalystActionRepository : IAnalystActionRepository
{
    public List<AnalystAction> Actions { get; } = [];

    public string? LastTickerFilter { get; private set; }

    public AnalystActionType? LastTypeFilter { get; private set; }

    public Task<int> UpsertAsync(IReadOnlyCollection<AnalystAction> actions, CancellationToken ct = default)
    {
        Actions.AddRange(actions);
        return Task.FromResult(actions.Count);
    }

    public Task<IReadOnlyList<AnalystAction>> QueryAsync(
        string? ticker, DateOnly since, AnalystActionType? actionType, int limit, CancellationToken ct = default)
    {
        LastTickerFilter = ticker;
        LastTypeFilter = actionType;

        var q = Actions.Where(a => a.ActionDate >= since);
        if (!string.IsNullOrWhiteSpace(ticker))
        {
            var upper = ticker.Trim().ToUpperInvariant();
            q = q.Where(a => a.Ticker == upper);
        }

        if (actionType is { } type)
        {
            q = q.Where(a => a.ActionType == type);
        }

        return Task.FromResult<IReadOnlyList<AnalystAction>>(
            q.OrderByDescending(a => a.ActionDate).Take(limit).ToList());
    }

    public Task<AnalystAction?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(Actions.FirstOrDefault(a => a.Id == id));
}

internal sealed class FakeBankingTotalsReader(params Guid[] userIds) : IBankingTotalsReader
{
    public Task<IReadOnlyList<Guid>> GetActiveUserIdsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Guid>>(userIds.ToList());

    public Task<decimal> GetTotalUsdAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult(0m);

    public Task<DateTime?> GetLatestSuccessfulSyncAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult<DateTime?>(DateTime.UtcNow);
}

internal sealed class FakeBrokerageReader(IReadOnlyList<BrokerageHoldingSummary>? holdings = null)
    : IBrokerageHoldingsReader
{
    public Task<IReadOnlyList<BrokerageHoldingSummary>> GetHoldingsAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult(holdings ?? []);
}

internal sealed class FakeSecEdgarService(IReadOnlyList<FundamentalFact>? facts = null) : ISecEdgarService
{
    public Task<IReadOnlyList<EdgarFiling>> GetRecentFilingsAsync(
        string ticker, IReadOnlyCollection<string>? formTypes, int limit, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<EdgarFiling>>([]);

    public Task<IReadOnlyList<FundamentalFact>> GetFundamentalsAsync(
        string ticker, int maxPerConcept, CancellationToken ct = default)
        => Task.FromResult(facts ?? []);
}

internal sealed class FakeMarketDataService(IReadOnlyList<DailyClose>? closes = null) : IMarketDataService
{
    public Task<IReadOnlyDictionary<string, QuoteCacheEntry>> GetQuotesAsync(
        IReadOnlyCollection<string> tickers, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<string, QuoteCacheEntry>>(
            new Dictionary<string, QuoteCacheEntry>());

    public Task<IReadOnlyList<DailyClose>> GetDailyClosesAsync(
        string ticker, DateOnly since, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DailyClose>>(
            (closes ?? []).Where(c => c.Date >= since).ToList());
}

internal sealed class FakeValuationDataService : IValuationDataService
{
    public Dictionary<string, ValuationCurrentMetrics?> Metrics { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> DefaultPeers { get; } = [];

    public Task<ValuationCurrentMetrics?> GetCurrentMetricsAsync(string ticker, CancellationToken ct = default)
        => Task.FromResult(Metrics.TryGetValue(ticker.Trim().ToUpperInvariant(), out var m) ? m : null);

    public Task<IReadOnlyList<string>> GetPeerSymbolsAsync(string ticker, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(DefaultPeers.ToList());
}

internal sealed class FakeValuationHistoryService(TrailingPeHistory? history = null) : IValuationHistoryService
{
    public Task<TrailingPeHistory> GetTrailingPeHistoryAsync(string ticker, CancellationToken ct = default)
        => Task.FromResult(history ?? new TrailingPeHistory(null, null));
}

internal sealed class FakeValuationSnapshotRepository : IValuationSnapshotRepository
{
    public List<ValuationSnapshot> Added { get; } = [];

    public Task AddAsync(ValuationSnapshot snapshot, CancellationToken ct = default)
    {
        Added.Add(snapshot);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ValuationSnapshot>> GetRecentAsync(
        string ticker, int limit, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ValuationSnapshot>>(
            Added.Where(s => s.Ticker == ticker.Trim().ToUpperInvariant()).ToList());
}

internal sealed class FakeRecommendationTrendRepository : IRecommendationTrendRepository
{
    public List<RecommendationTrend> Trends { get; } = [];

    public Task<int> UpsertAsync(IReadOnlyList<RecommendationTrend> trends, CancellationToken ct = default)
    {
        Trends.AddRange(trends);
        return Task.FromResult(trends.Count);
    }

    public Task<IReadOnlyList<RecommendationTrend>> GetLatestAsync(
        string ticker, int months, CancellationToken ct = default)
    {
        var upper = ticker.Trim().ToUpperInvariant();
        return Task.FromResult<IReadOnlyList<RecommendationTrend>>(Trends
            .Where(t => t.Ticker == upper)
            .OrderByDescending(t => t.Period)
            .Take(months)
            .ToList());
    }
}

/// <summary>
/// In-memory <see cref="INewsSourceRepository"/>. Reads hand back a *copy*, mirroring the real
/// repository's <c>AsNoTracking()</c> queries: a caller that mutates what it read and forgets to call
/// <see cref="UpdateAsync"/> persists nothing here either, so tests exercise the same contract the
/// database enforces.
/// </summary>
internal sealed class FakeNewsSourceRepository : INewsSourceRepository
{
    public List<NewsSource> Sources { get; } = [];

    public Task<IReadOnlyList<NewsSource>> ListEnabledAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<NewsSource>>(Sources.Where(s => s.Enabled).Select(Copy).ToList());

    public Task<IReadOnlyList<NewsSource>> ListDisabledAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<NewsSource>>(Sources.Where(s => !s.Enabled).Select(Copy).ToList());

    public Task<IReadOnlyList<NewsSource>> ListAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<NewsSource>>(Sources.Select(Copy).ToList());

    public Task<NewsSource?> GetByUrlAsync(string url, CancellationToken ct = default)
    {
        var match = Sources.FirstOrDefault(s => s.Url == url);
        return Task.FromResult(match is null ? null : Copy(match));
    }

    public Task<Guid> AddAsync(NewsSource source, CancellationToken ct = default)
    {
        Sources.Add(Copy(source));
        return Task.FromResult(source.Id);
    }

    public Task UpdateAsync(NewsSource source, CancellationToken ct = default)
    {
        var index = Sources.FindIndex(s => s.Id == source.Id);
        if (index >= 0)
        {
            Sources[index] = Copy(source);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(NewsSource source, CancellationToken ct = default)
    {
        Sources.RemoveAll(s => s.Id == source.Id);
        return Task.CompletedTask;
    }

    private static NewsSource Copy(NewsSource s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Kind = s.Kind,
        Url = s.Url,
        Keywords = [.. s.Keywords],
        ThesisId = s.ThesisId,
        Enabled = s.Enabled,
        ConsecutiveFailures = s.ConsecutiveFailures,
        LastSuccessAt = s.LastSuccessAt,
        LastFailureReason = s.LastFailureReason,
        CreatedAt = s.CreatedAt,
    };
}

/// <summary>
/// In-memory <see cref="INewsRepository"/> that keeps what was inserted, so a test can assert on the
/// articles an ingestion path actually persisted rather than only on its return count.
/// </summary>
internal sealed class FakeNewsRepository : INewsRepository
{
    public List<NewsArticle> Inserted { get; } = [];

    public Task<IReadOnlyList<NewsArticle>> SearchAsync(
        string? query,
        IReadOnlyCollection<string>? tickers,
        Guid? thesisId,
        DateTimeOffset? since,
        int limit,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<NewsArticle>>([]);

    public Task<IReadOnlyList<NewsArticle>> GetForTickerAsync(
        string ticker, DateTimeOffset? since, int limit, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<NewsArticle>>([]);

    public Task<int> InsertNewAsync(IReadOnlyCollection<NewsArticle> articles, CancellationToken ct = default)
    {
        Inserted.AddRange(articles);
        return Task.FromResult(articles.Count);
    }
}

/// <summary><see cref="IMarketNewsService"/> double; only the registered-source feed path is exercised.</summary>
internal sealed class FakeMarketNewsService(IReadOnlyList<NewsArticle>? feedArticles = null) : IMarketNewsService
{
    public string? RequestedLabel { get; private set; }

    public Task<int> IngestForTickersAsync(IReadOnlyCollection<string> tickers, CancellationToken ct = default)
        => Task.FromResult(0);

    public Task<int> IngestFedPressAsync(CancellationToken ct = default) => Task.FromResult(0);

    public Task<IReadOnlyList<NewsArticle>> FetchFeedArticlesAsync(
        string url, string sourceLabel, CancellationToken ct = default)
    {
        RequestedLabel = sourceLabel;
        return Task.FromResult(feedArticles ?? []);
    }
}

/// <summary>
/// <see cref="INewsPageSource"/> double that claims one URL and either yields the given candidates or
/// throws — the two outcomes the health/recovery paths branch on.
/// </summary>
internal sealed class FakeNewsPageSource(
    string url, IReadOnlyList<NewsPageArticle>? articles = null, Exception? failure = null) : INewsPageSource
{
    public int FetchCount { get; private set; }

    public bool CanHandle(string candidateUrl) => candidateUrl == url;

    public Task<IReadOnlyList<NewsPageArticle>> FetchAsync(string candidateUrl, CancellationToken ct = default)
    {
        FetchCount++;
        return failure is not null
            ? Task.FromException<IReadOnlyList<NewsPageArticle>>(failure)
            : Task.FromResult(articles ?? []);
    }
}
