using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Fundamentals;

namespace FinanceSentry.Mcp.Tests.IntegrationTests;

/// <summary>
/// Deterministic test double for <see cref="IFundamentalsService"/> — the parity tests never hit
/// a live provider. Returns whatever facts were seeded for the requested ticker.
/// </summary>
public sealed class FakeFundamentalsService(IReadOnlyDictionary<string, IReadOnlyList<FundamentalFact>> factsByTicker)
    : IFundamentalsService
{
    public Task<FundamentalsResult> GetFundamentalsAsync(
        string ticker, int maxPerConcept, CancellationToken ct = default)
    {
        IReadOnlyList<FundamentalFact> facts = factsByTicker.TryGetValue(ticker, out var seeded) ? seeded : [];
        var coverage = facts.Count > 0
            ? new FundamentalsCoverage(FundamentalsCoverageStatus.Covered, null, null, [])
            : new FundamentalsCoverage(FundamentalsCoverageStatus.NoSourceAvailable, null, "no facts seeded", []);
        return Task.FromResult(new FundamentalsResult(ticker, facts, coverage));
    }
}

/// <summary>
/// Deterministic test double for <see cref="IMarketDataService"/> — returns no price history by
/// default (parity tests that exercise fundamentals-based triggers only), or the seeded quotes
/// (020 track-record parity tests) when <paramref name="quotesByTicker"/> is supplied.
/// </summary>
public sealed class FakeMarketDataService(
    IReadOnlyDictionary<string, QuoteCacheEntry>? quotesByTicker = null,
    IReadOnlyDictionary<string, IReadOnlyList<DailyClose>>? closesByTicker = null) : IMarketDataService
{
    public Task<IReadOnlyDictionary<string, QuoteCacheEntry>> GetQuotesAsync(
        IReadOnlyCollection<string> tickers, CancellationToken ct = default)
    {
        if (quotesByTicker is null)
        {
            return Task.FromResult<IReadOnlyDictionary<string, QuoteCacheEntry>>(
                new Dictionary<string, QuoteCacheEntry>());
        }

        var matched = tickers
            .Where(quotesByTicker.ContainsKey)
            .ToDictionary(t => t, t => quotesByTicker[t]);
        return Task.FromResult<IReadOnlyDictionary<string, QuoteCacheEntry>>(matched);
    }

    public Task<IReadOnlyList<DailyClose>> GetDailyClosesAsync(
        string ticker, DateOnly since, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DailyClose>>(
            closesByTicker is not null && closesByTicker.TryGetValue(ticker, out var closes)
                ? closes.Where(c => c.Date >= since).ToList()
                : []);
}
