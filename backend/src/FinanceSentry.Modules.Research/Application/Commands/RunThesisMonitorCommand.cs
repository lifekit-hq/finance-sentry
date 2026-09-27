namespace FinanceSentry.Modules.Research.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Domain.ThesisMonitor;
using Microsoft.Extensions.Logging;

public record RunThesisMonitorCommand(Guid UserId) : ICommand<ThesisMonitorRunSummary>;

/// <summary>
/// Evaluates every trigger of every thesis owned by a user and marks unbroken theses broken on
/// the first breaching trigger (OR semantics). Also auto-clears a broken thesis when a fresh
/// evaluation holds for every evaluable trigger (US3) — but never on an all-non-evaluable result,
/// since missing data must not un-break a thesis (FR-013).
/// </summary>
public class RunThesisMonitorCommandHandler(
    IThesisRepository thesisRepo,
    ISecEdgarService secEdgar,
    IMarketDataService marketData,
    IAlertGeneratorService alertGenerator,
    IThesisEventRecorder eventRecorder,
    ILogger<RunThesisMonitorCommandHandler> logger)
    : ICommandHandler<RunThesisMonitorCommand, ThesisMonitorRunSummary>
{
    private const int MaxFundamentalsPerConcept = 8;

    // Calendar-day padding over a trading-day count so weekends/holidays don't starve the window
    // (same 1.5x heuristic Radar's StructureQueryService.LookbackSince uses).
    private const decimal RelativeReturnLookbackPaddingFactor = 1.5m;
    private const int RelativeReturnLookbackPaddingDays = 10;

    public async Task<ThesisMonitorRunSummary> Handle(RunThesisMonitorCommand cmd, CancellationToken ct)
    {
        var theses = await thesisRepo.ListAsync(cmd.UserId, ct);

        var thesesEvaluated = 0;
        var triggersEvaluated = 0;
        var breaksRaised = 0;
        var breaksCleared = 0;
        var skipped = 0;
        var errors = 0;

        var fundamentalsCache = new Dictionary<string, IReadOnlyList<FundamentalFact>>(StringComparer.OrdinalIgnoreCase);
        var closesCache = new Dictionary<string, IReadOnlyList<DailyClose>>(StringComparer.OrdinalIgnoreCase);

        foreach (var thesis in theses)
        {
            if (thesis.InvalidationTriggers.Count == 0)
            {
                // Bug fix: a thesis with zero triggers must not retain an orphaned break.
                // Previously this path unconditionally skipped, leaving BrokenAt/BrokenReason
                // stuck even after the offending trigger was deleted.
                if (thesis.BrokenAt is not null)
                {
                    thesis.BrokenAt = null;
                    thesis.BrokenReason = null;
                    await thesisRepo.UpsertAsync(thesis, ct);
                    await alertGenerator.ResolveThesisBreakAlertAsync(thesis.UserId, thesis.Id, ct);
                    await TryRecordEventAsync(thesis, ThesisEventType.Unbroken, decisionNote: null, ct);
                    breaksCleared++;
                }
                else
                {
                    skipped++;
                }

                continue;
            }

            try
            {
                thesesEvaluated++;

                var evaluations = new List<(ThesisInvalidationTrigger Trigger, TriggerVerdict Verdict)>();
                foreach (var trigger in thesis.InvalidationTriggers)
                {
                    triggersEvaluated++;
                    evaluations.Add((trigger, await EvaluateTriggerAsync(
                        trigger, thesis, fundamentalsCache, closesCache, ct)));
                }

                var verdicts = evaluations.Select(e => e.Verdict).ToList();
                var firstBreach = verdicts.OfType<TriggerVerdict.Breached>().FirstOrDefault();
                var allNonEvaluable = verdicts.Count > 0 && verdicts.All(v => v is TriggerVerdict.NonEvaluable);

                if (firstBreach is not null && thesis.BrokenAt is null)
                {
                    thesis.BrokenAt = DateTimeOffset.UtcNow;
                    thesis.BrokenReason = ComposeReason(firstBreach);
                    await thesisRepo.UpsertAsync(thesis, ct);
                    await alertGenerator.GenerateThesisBreakAlertAsync(
                        thesis.UserId, thesis.Id, thesis.Ticker, thesis.BrokenReason, ct);
                    await TryRecordEventAsync(
                        thesis, ThesisEventType.Broken, thesis.BrokenReason, ct);
                    breaksRaised++;
                }
                else if (firstBreach is null && allNonEvaluable)
                {
                    skipped++;
                }
                else if (firstBreach is null && thesis.BrokenAt is not null && CanUnbreak(thesis, evaluations))
                {
                    // Broken→cleared: none breached, not all-non-evaluable, AND the trigger that
                    // originally broke the thesis was itself re-evaluated on fresh data. "Cleared
                    // in fresh data" (FR-011) — a degraded proxy fetch making the breaching
                    // trigger non-evaluable must not un-break the thesis.
                    thesis.BrokenAt = null;
                    thesis.BrokenReason = null;
                    await thesisRepo.UpsertAsync(thesis, ct);
                    await alertGenerator.ResolveThesisBreakAlertAsync(thesis.UserId, thesis.Id, ct);
                    await TryRecordEventAsync(thesis, ThesisEventType.Unbroken, decisionNote: null, ct);
                    breaksCleared++;
                }

                // firstBreach is not null && thesis already broken: still-broken, no-op (dedup lives
                // in the alert generator too). firstBreach is null && not all non-evaluable && not
                // previously broken: held, no-op.
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex, "Thesis monitor failed for thesis {ThesisId} ({Ticker})", thesis.Id, thesis.Ticker);
                errors++;
            }
        }

        return new ThesisMonitorRunSummary(
            thesesEvaluated, triggersEvaluated, breaksRaised, breaksCleared, skipped, errors);
    }

    private async Task<TriggerVerdict> EvaluateTriggerAsync(
        ThesisInvalidationTrigger trigger,
        InvestmentThesis thesis,
        Dictionary<string, IReadOnlyList<FundamentalFact>> fundamentalsCache,
        Dictionary<string, IReadOnlyList<DailyClose>> closesCache,
        CancellationToken ct)
    {
        var targetTicker = trigger.ProxyTicker ?? thesis.Ticker;

        if (ThesisMetric.IsRelativeMetric(trigger.Metric))
        {
            // Needs windowDays + consecutivePeriods of trailing trading-day history, which can
            // reach further back than the thesis itself — unlike price_return/drawdown, this isn't
            // measured "since entry".
            var since = RelativeReturnLookbackSince(trigger);
            var subjectCloses = await GetClosesAsync(
                targetTicker, since, closesCache, ct, cacheKey: $"{targetTicker}::relative_return");

            var benchmarkTicker = trigger.BenchmarkTicker!;
            var benchmarkCloses = await GetClosesAsync(
                benchmarkTicker, since, closesCache, ct, cacheKey: $"{benchmarkTicker}::relative_return");

            return ThesisBreakEvaluator.Evaluate(
                trigger, thesis.CreatedAt, [], subjectCloses, thesis.EntryPrice, benchmarkCloses);
        }

        if (ThesisMetric.IsPriceMetric(trigger.Metric))
        {
            var closes = await GetClosesAsync(targetTicker, thesis.CreatedAt, closesCache, ct);
            return ThesisBreakEvaluator.Evaluate(trigger, thesis.CreatedAt, [], closes, thesis.EntryPrice);
        }

        var facts = await GetFundamentalsAsync(targetTicker, fundamentalsCache, ct);
        return ThesisBreakEvaluator.Evaluate(trigger, thesis.CreatedAt, facts, []);
    }

    private static DateTimeOffset RelativeReturnLookbackSince(ThesisInvalidationTrigger trigger)
    {
        var tradingDays = trigger.WindowDays!.Value + trigger.ConsecutivePeriods;
        var calendarDays = (int)Math.Ceiling(tradingDays * RelativeReturnLookbackPaddingFactor)
            + RelativeReturnLookbackPaddingDays;
        return DateTimeOffset.UtcNow.AddDays(-calendarDays);
    }

    private async Task<IReadOnlyList<FundamentalFact>> GetFundamentalsAsync(
        string ticker,
        Dictionary<string, IReadOnlyList<FundamentalFact>> cache,
        CancellationToken ct)
    {
        if (cache.TryGetValue(ticker, out var cached))
        {
            return cached;
        }

        var facts = await secEdgar.GetFundamentalsAsync(ticker, MaxFundamentalsPerConcept, ct);
        cache[ticker] = facts;
        return facts;
    }

    private async Task<IReadOnlyList<DailyClose>> GetClosesAsync(
        string ticker,
        DateTimeOffset since,
        Dictionary<string, IReadOnlyList<DailyClose>> cache,
        CancellationToken ct,
        string? cacheKey = null)
    {
        var key = cacheKey ?? ticker;
        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var closes = await marketData.GetDailyClosesAsync(
            ticker, DateOnly.FromDateTime(since.UtcDateTime), ct);
        cache[key] = closes;
        return closes;
    }

    /// <summary>
    /// Records a Broken/Unbroken lifecycle event (020 track-record hook). Never allowed to fail or
    /// abort the monitor run — a recorder/quote failure is caught and logged, matching the
    /// per-thesis try/catch semantics already used for trigger evaluation above.
    /// </summary>
    private async Task TryRecordEventAsync(
        InvestmentThesis thesis, ThesisEventType eventType, string? decisionNote, CancellationToken ct)
    {
        try
        {
            await eventRecorder.RecordAsync(
                thesis.UserId, ThesisSubjectType.Thesis, thesis.Id, thesis.Ticker, eventType, decisionNote, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex, "Thesis event recording failed for thesis {ThesisId} ({Ticker}, {EventType})",
                thesis.Id, thesis.Ticker, eventType);
        }
    }

    /// <summary>
    /// The triggers matching the metric named in <see cref="InvestmentThesis.BrokenReason"/> must
    /// all be evaluable (Held) before the break clears. When no trigger matches (triggers edited
    /// since the break), fall back to permitting the clear — none-breached + some-evaluable.
    /// </summary>
    private static bool CanUnbreak(
        InvestmentThesis thesis,
        IReadOnlyList<(ThesisInvalidationTrigger Trigger, TriggerVerdict Verdict)> evaluations)
    {
        var reason = thesis.BrokenReason;
        if (string.IsNullOrWhiteSpace(reason))
        {
            return true;
        }

        var matching = evaluations
            .Where(e => reason.Contains(e.Trigger.Metric, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matching.Count == 0 || matching.All(e => e.Verdict is TriggerVerdict.Held);
    }

    private static string ComposeReason(TriggerVerdict.Breached breach)
    {
        var observed = string.Join(", ", breach.ObservedValues.Select(v => v.ToString("0.####")));
        var periods = string.Join(", ", breach.Periods);
        return $"{breach.Metric} {breach.Direction} {breach.Threshold:0.####} " +
               $"— observed [{observed}] over [{periods}]";
    }
}
