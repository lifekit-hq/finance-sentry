namespace FinanceSentry.Modules.Research.Tests.Recommendations;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using FinanceSentry.Modules.Research.Domain.Recommendations;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>List-backed recommendation store that keeps the one-pending-per-ticker rule the partial unique index enforces.</summary>
internal sealed class InMemoryRecommendationRepository : IRecommendationRepository
{
    public List<Recommendation> Rows { get; } = [];

    public Task<Recommendation?> GetAsync(Guid userId, Guid id, CancellationToken ct = default)
        => Task.FromResult(Rows.FirstOrDefault(r => r.UserId == userId && r.Id == id));

    public Task<Recommendation?> FindPendingAsync(Guid userId, string ticker, CancellationToken ct = default)
        => Task.FromResult(Rows.FirstOrDefault(r => r.UserId == userId && r.Ticker == ticker
            && r.Status == RecommendationStatus.Pending && r.ExpiresAt > DateTimeOffset.UtcNow));

    public Task<IReadOnlyList<Recommendation>> ListPendingAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Recommendation>>(Rows
            .Where(r => r.UserId == userId && r.Status == RecommendationStatus.Pending && r.ExpiresAt > DateTimeOffset.UtcNow)
            .OrderBy(r => r.CreatedAt).ToList());

    public Task<IReadOnlyList<Recommendation>> ListClosedSinceAsync(Guid userId, DateTimeOffset since, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Recommendation>>(Rows
            .Where(r => r.UserId == userId && r.Status != RecommendationStatus.Pending && (r.DecidedAt ?? r.ExpiresAt) >= since)
            .OrderByDescending(r => r.DecidedAt ?? r.ExpiresAt).ToList());

    public Task<Recommendation?> FindPendingUnscopedAsync(Guid userId, string ticker, CancellationToken ct = default)
        => Task.FromResult(Rows.FirstOrDefault(r => r.UserId == userId && r.Ticker == ticker && r.Status == RecommendationStatus.Pending));

    public Task<bool> ExistsForTriggerSinceUnscopedAsync(
        Guid userId, string ticker, RecommendationTriggerKind kind, string triggerRef, DateTimeOffset since, CancellationToken ct = default)
        => Task.FromResult(Rows.Any(r => r.UserId == userId && r.Ticker == ticker && r.TriggerKind == kind
            && r.TriggerRef == triggerRef && r.CreatedAt >= since));

    public Task<IReadOnlyList<Recommendation>> ListExpiredUnscopedAsync(DateTimeOffset asOf, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Recommendation>>(Rows
            .Where(r => r.Status == RecommendationStatus.Pending && r.ExpiresAt <= asOf).ToList());

    public Task<IReadOnlyList<Recommendation>> ListOutcomeOpenUnscopedAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Recommendation>>(Rows
            .Where(r => r.Status is RecommendationStatus.Confirmed or RecommendationStatus.Rejected
                && r.DecidedAt != null && r.OutcomeScoredAt == null).ToList());

    public Task<IReadOnlyList<Recommendation>> ListClosedUnscopedAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Recommendation>>(Rows
            .Where(r => r.UserId == userId && r.Status != RecommendationStatus.Pending).ToList());

    public Task AddPendingAsync(Recommendation recommendation, Recommendation? supersedes = null, CancellationToken ct = default)
    {
        if (Rows.Any(r => r.UserId == recommendation.UserId && r.Ticker == recommendation.Ticker
            && r.Status == RecommendationStatus.Pending))
        {
            throw new RecommendationConflictException(recommendation.Ticker);
        }

        Rows.Add(recommendation);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Recommendation recommendation, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Records every journal write with its subject type, so tests can assert the decision trail.</summary>
internal sealed class JournalRecorder : IThesisEventRecorder
{
    public List<(ThesisSubjectType SubjectType, Guid SubjectId, ThesisEventType EventType, string? Note)> Events { get; } = [];

    public Task RecordAsync(
        Guid userId, ThesisSubjectType subjectType, Guid subjectId, string ticker, ThesisEventType eventType,
        string? decisionNote = null, CancellationToken ct = default)
    {
        Events.Add((subjectType, subjectId, eventType, decisionNote));
        return Task.CompletedTask;
    }
}

internal sealed class FixedBookFigures(BookFigures book) : IBookFiguresService
{
    public Task<BookFigures> ReadAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(book);
}

internal sealed class FixedQuotes(IReadOnlyDictionary<string, decimal> prices, string currency = "USD") : IMarketDataService
{
    public Task<IReadOnlyDictionary<string, QuoteCacheEntry>> GetQuotesAsync(
        IReadOnlyCollection<string> tickers, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<string, QuoteCacheEntry>>(tickers
            .Where(prices.ContainsKey)
            .ToDictionary(t => t, t => new QuoteCacheEntry { Ticker = t, Price = prices[t], Currency = currency }));

    public Task<IReadOnlyList<DailyClose>> GetDailyClosesAsync(string ticker, DateOnly since, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DailyClose>>([]);
}

internal static class RecommendationTestBook
{
    /// <summary>A book worth <paramref name="totalUsd"/> holding the given positions (IBKR equities unless named).</summary>
    public static BookFigures With(decimal totalUsd, params BookFigurePosition[] positions)
        => new(0m, 0m, 0m, positions.Sum(p => p.UsdValue), totalUsd, positions, false, []);

    public static BookFigurePosition Stock(string symbol, decimal quantity, decimal usdValue, string provider = "ibkr")
        => new(symbol, "Equity", quantity, null, usdValue, provider);

    public static RecommendationPositionReader Reader(BookFigures book, IReadOnlyDictionary<string, decimal> prices)
        => new(new FixedBookFigures(book), new FixedQuotes(prices), NullLogger<RecommendationPositionReader>.Instance);
}
