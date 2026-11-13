namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Logging;

/// <summary>
/// Records price-stamped lifecycle events (R1/R2). Quote lookups go through <see cref="IMarketDataService"/>;
/// any failure (exception or missing quote) still appends the event, marked <see cref="ThesisEvent.PricesPending"/>
/// so the weekly snapshot job can backfill later (FR-003) — the originating write is never blocked.
/// </summary>
public class ThesisEventRecorder(
    IThesisEventRepository repo,
    IMarketDataService marketData,
    ILogger<ThesisEventRecorder> logger) : IThesisEventRecorder
{
    public const string DefaultBenchmarkTicker = "SPY";

    public Task RecordAsync(
        Guid userId,
        ThesisSubjectType subjectType,
        Guid subjectId,
        string ticker,
        ThesisEventType eventType,
        string? decisionNote = null,
        CancellationToken ct = default)
        => RecordAuditedAsync(
            userId, subjectType, subjectId, ticker, eventType, ThesisEventOrigin.User, audit: null, decisionNote, ct);

    public async Task RecordAuditedAsync(
        Guid userId,
        ThesisSubjectType subjectType,
        Guid subjectId,
        string ticker,
        ThesisEventType eventType,
        ThesisEventOrigin origin,
        ThesisAudit? audit,
        string? decisionNote = null,
        CancellationToken ct = default)
    {
        if (eventType == ThesisEventType.Created)
        {
            var existingCreated = await repo.GetLatestForSubjectUnscopedAsync(userId, subjectType, subjectId, ct);
            if (existingCreated is not null)
            {
                // Exactly one Created event per subject (data-model.md invariant) — idempotent no-op.
                return;
            }
        }

        var stamped = eventType is not (ThesisEventType.Revised or ThesisEventType.Reverted);
        var (subjectPrice, benchmarkPrice, pricesPending) = stamped
            ? await TryGetPricesAsync(ticker, ct)
            : (null, null, false);

        var thesisEvent = new ThesisEvent
        {
            UserId = userId,
            SubjectType = subjectType,
            SubjectId = subjectId,
            Ticker = ticker,
            EventType = eventType,
            Timestamp = DateTimeOffset.UtcNow,
            SubjectPrice = subjectPrice,
            BenchmarkPrice = benchmarkPrice,
            BenchmarkTicker = DefaultBenchmarkTicker,
            PricesPending = pricesPending,
            DecisionNote = decisionNote,
            Origin = origin,
            Audit = audit,
        };

        await repo.AppendAsync(thesisEvent, ct);
    }

    private async Task<(decimal? SubjectPrice, decimal? BenchmarkPrice, bool PricesPending)> TryGetPricesAsync(
        string ticker, CancellationToken ct)
    {
        try
        {
            var quotes = await marketData.GetQuotesAsync([ticker, DefaultBenchmarkTicker], ct);

            var hasSubject = quotes.TryGetValue(ticker, out var subjectQuote);
            var hasBenchmark = quotes.TryGetValue(DefaultBenchmarkTicker, out var benchmarkQuote);

            if (hasSubject && hasBenchmark)
            {
                return (subjectQuote!.Price, benchmarkQuote!.Price, false);
            }

            logger.LogWarning(
                "ThesisEventRecorder: quote missing for {Ticker} or {Benchmark} — recording pending",
                ticker, DefaultBenchmarkTicker);
            return (null, null, true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex, "ThesisEventRecorder: quote lookup failed for {Ticker} — recording pending", ticker);
            return (null, null, true);
        }
    }
}
