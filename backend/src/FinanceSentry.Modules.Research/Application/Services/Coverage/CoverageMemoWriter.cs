namespace FinanceSentry.Modules.Research.Application.Services.Coverage;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain.Coverage;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>What one memo run did.</summary>
public sealed record CoverageMemoRunSummary(int Written, int Unstructured, int DeferredByDailyCap, int DeferredByBudget, int Failed)
{
    public static CoverageMemoRunSummary Empty { get; } = new(0, 0, 0, 0, 0);
}

/// <summary>
/// The research memo on coverage entrants (discovery funnel stage 6), the funnel's only model call. For each ranked
/// coverage add that has no memo yet it makes one single-turn call over evidence the pipeline already stored and keeps
/// the reply as a <see cref="CoverageMemo"/>. Three limits keep the spend bounded and are checked before every call:
/// the mode (<see cref="CoverageMemoMode.Off"/> by default makes no call), <see cref="CoverageOptions.MaxMemosPerDay"/>
/// across everyone, and a rolling seven-day budget (<see cref="CoverageOptions.MemoWeeklyBudgetUsd"/>) that the call's
/// worst-case cost must fit under on top of what was already recorded. Entrants a limit passed over stay eligible for
/// <see cref="CoverageOptions.MemoLookbackDays"/>. Logged only: nothing reads a memo to act.
/// </summary>
public sealed class CoverageMemoWriter(
    ICoverageMemoRepository memos,
    ICoverageRepository coverage,
    IQueryHandler<GetScorecardQuery, ScorecardResult> scorecard,
    IOptions<CoverageOptions> options,
    ILogger<CoverageMemoWriter> logger,
    IMemoLanguageModel? model = null)
{
    private const int CharsPerTokenFloor = 3;
    private const int TokensPerMillion = 1_000_000;
    private const int CostDecimals = 4;
    private const int WeekDays = 7;
    private const int MaxStoredRawChars = 20_000;

    private readonly CoverageOptions _options = options.Value;

    public async Task<CoverageMemoRunSummary> WriteAllAsync(DateTimeOffset asOf, CancellationToken ct = default)
    {
        if (_options.MemoMode == CoverageMemoMode.Off)
        {
            logger.LogDebug("Coverage memos: mode is Off, no call made");
            return CoverageMemoRunSummary.Empty;
        }

        if (model is not { IsConfigured: true })
        {
            logger.LogWarning("Coverage memos: mode is {Mode} but no model is configured; no call made", _options.MemoMode);
            return CoverageMemoRunSummary.Empty;
        }

        var today = new DateTimeOffset(asOf.UtcDateTime.Date, TimeSpan.Zero);
        var (writtenToday, _) = await memos.TotalsSinceUnscopedAsync(today, ct);
        var (_, spentThisWeek) = await memos.TotalsSinceUnscopedAsync(asOf.AddDays(-WeekDays), ct);

        var state = new RunState(Math.Max(0, _options.MaxMemosPerDay - writtenToday), spentThisWeek);
        var users = await coverage.ListUserIdsUnscopedAsync(ct);
        foreach (var userId in users)
        {
            if (state.Stopped)
            {
                break;
            }

            await WriteForUserAsync(userId, asOf, state, ct);
        }

        var summary = new CoverageMemoRunSummary(
            state.Written, state.Unstructured, state.DeferredByDailyCap, state.DeferredByBudget, state.Failed);
        logger.LogInformation(
            "Coverage memos: written {Written} ({Unstructured} unstructured), deferred by the daily cap {Daily}, deferred by the weekly budget {Budget}, failed {Failed}, spent this week ${Spent:0.0000} of ${Cap:0.00}",
            summary.Written, summary.Unstructured, summary.DeferredByDailyCap, summary.DeferredByBudget, summary.Failed,
            state.SpentThisWeek, _options.MemoWeeklyBudgetUsd);

        if (summary.Failed > 0)
        {
            throw new InvalidOperationException(
                $"Coverage memos: {summary.Failed} call(s) failed; the others were saved and the failed entrants are retried while they stay eligible.");
        }

        return summary;
    }

    private async Task WriteForUserAsync(Guid userId, DateTimeOffset asOf, RunState state, CancellationToken ct)
    {
        var entrants = await memos.ListEntrantsWithoutMemoUnscopedAsync(userId, asOf.AddDays(-_options.MemoLookbackDays), ct);
        if (entrants.Count == 0)
        {
            return;
        }

        var members = (await coverage.ListMembersUnscopedAsync(userId, ct))
            .ToDictionary(m => m.Ticker, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entrants)
        {
            if (state.RemainingToday <= 0)
            {
                state.DeferredByDailyCap++;
                continue;
            }

            if (state.Stopped)
            {
                state.DeferredByBudget += state.BudgetHit ? 1 : 0;
                continue;
            }

            await WriteOneAsync(userId, entry, members.GetValueOrDefault(entry.Ticker), asOf, state, ct);
        }
    }

    private async Task WriteOneAsync(
        Guid userId, CoverageJournalEntry entry, CoverageMember? member, DateTimeOffset asOf, RunState state, CancellationToken ct)
    {
        ScorecardResult card;
        try
        {
            card = await scorecard.Handle(new GetScorecardQuery(entry.Ticker), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The evidence is the point of the memo: a call without the scorecard is not paid for.
            logger.LogWarning(ex, "Coverage memos: the scorecard for {Ticker} could not be read; the memo waits", entry.Ticker);
            return;
        }

        var events = await coverage.ListEventsForTickerUnscopedAsync(userId, entry.Ticker, _options.MemoEventsInPrompt, ct);
        var user = CoverageMemoPrompt.User(new CoverageMemoEvidence(entry, member, card, events), _options.MemoMaxInputChars);

        var worstCase = Price(
            (CoverageMemoPrompt.System.Length + user.Length + CharsPerTokenFloor - 1) / CharsPerTokenFloor,
            _options.MemoMaxOutputTokens);
        if (state.SpentThisWeek + worstCase > _options.MemoWeeklyBudgetUsd)
        {
            state.BudgetHit = true;
            state.DeferredByBudget++;
            logger.LogWarning(
                "Coverage memos: ${Spent:0.0000} spent in the last {Days} days and a call for {Ticker} could cost ${Worst:0.0000}, over the ${Cap:0.00} budget; no more memos this run",
                state.SpentThisWeek, WeekDays, entry.Ticker, worstCase, _options.MemoWeeklyBudgetUsd);
            return;
        }

        MemoModelReply reply;
        try
        {
            reply = await model!.CompleteAsync(
                new MemoModelRequest(CoverageMemoPrompt.System, user, _options.MemoMaxOutputTokens, _options.MemoModelId), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A provider failure is usually systemic (key, quota, outage): stop rather than retry every entrant.
            state.Failed++;
            logger.LogError(ex, "Coverage memos: the model call for {Ticker} failed; no more memos this run", entry.Ticker);
            return;
        }

        var cost = decimal.Round(Price(reply.InputTokens, reply.OutputTokens), CostDecimals);
        var content = CoverageMemoParser.TryParse(reply.Text);
        var memo = new CoverageMemo
        {
            UserId = userId,
            Ticker = entry.Ticker,
            JournalEntryId = entry.Id,
            CreatedAt = asOf,
            ModelId = reply.ModelId,
            InputTokens = reply.InputTokens,
            OutputTokens = reply.OutputTokens,
            CostUsd = cost,
            Status = content is null ? CoverageMemoStatus.Unstructured : CoverageMemoStatus.Structured,
            Verdict = content?.Verdict,
            Body = content is null
                ? reply.Text[..Math.Min(reply.Text.Length, MaxStoredRawChars)]
                : CoverageMemoParser.Serialize(content),
        };

        // The money is spent whether or not the row lands, so the week's total counts it either way.
        state.SpentThisWeek += cost;
        state.RemainingToday--;
        if (!await memos.AddUnscopedAsync(memo, ct))
        {
            return;
        }

        state.Written++;
        if (content is null)
        {
            state.Unstructured++;
            logger.LogWarning("Coverage memos: the reply for {Ticker} was not the memo shape; kept as raw text", entry.Ticker);
        }
    }

    private decimal Price(int inputTokens, int outputTokens)
        => (inputTokens * _options.MemoInputUsdPerMillionTokens / TokensPerMillion)
            + (outputTokens * _options.MemoOutputUsdPerMillionTokens / TokensPerMillion);

    private sealed class RunState(int remainingToday, decimal spentThisWeek)
    {
        public int RemainingToday { get; set; } = remainingToday;

        public decimal SpentThisWeek { get; set; } = spentThisWeek;

        public bool BudgetHit { get; set; }

        /// <summary>The run makes no more calls: the weekly budget is reached or the provider failed.</summary>
        public bool Stopped => BudgetHit || Failed > 0;

        public int Written { get; set; }

        public int Unstructured { get; set; }

        public int DeferredByDailyCap { get; set; }

        public int DeferredByBudget { get; set; }

        public int Failed { get; set; }
    }
}
