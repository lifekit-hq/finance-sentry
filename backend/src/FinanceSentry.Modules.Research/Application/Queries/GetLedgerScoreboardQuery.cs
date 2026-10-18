namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Recommendations;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Options;

public sealed record GetLedgerScoreboardQuery(Guid UserId, DateTimeOffset AsOf) : IQuery<LedgerScoreboardDto>;

/// <summary>
/// The Ledger scoreboard (#986, rallies comparison option B1). Adds no scoring method of its own: a decided proposal
/// is scored by the outcome job (#1069) and signed by <see cref="RecommendationValue"/>; a paper pick is graded from its
/// <c>Created</c> price stamp to its horizon stamp (<see cref="PaperPickHorizon"/>); every row's gross and net excess
/// comes from spec 020's <see cref="IThesisPerformanceCalculator"/> with <see cref="FrictionConfig"/>, and the sample
/// floor is <see cref="GetTrackRecordQueryHandler.MinimumClosedSampleSize"/>. Reads unscoped, because the weekly brief
/// reaches it with no person in scope.
/// </summary>
public sealed class GetLedgerScoreboardQueryHandler(
    IRecommendationRepository recommendations,
    ICandidateRepository candidates,
    IThesisEventRepository events,
    IThesisPerformanceCalculator calculator,
    IOptions<FrictionConfig> frictionOptions)
    : IQueryHandler<GetLedgerScoreboardQuery, LedgerScoreboardDto>
{
    public const string LedgerAsProposed = "LedgerAsProposed";
    public const string OwnerAsDecided = "OwnerAsDecided";
    public const string PaperPicks = "PaperPicks";
    public const string Benchmark = "S&P 500";

    private const string PickKind = "Pick";
    private const int FigureDecimals = 4;
    private const decimal Percent = 100m;

    /// <summary>
    /// A proposal's outcome is stored as two returns, not prices: the calculator prices each side from this base so its
    /// gross excess is exactly the stored difference and its net applies the same friction a thesis gets.
    /// </summary>
    private const decimal SyntheticBasePrice = 100m;

    private static readonly CandidateSource[] PickSources = [CandidateSource.User, CandidateSource.Ledger, CandidateSource.Scan];

    public async Task<LedgerScoreboardDto> Handle(GetLedgerScoreboardQuery query, CancellationToken ct)
    {
        var closed = await recommendations.ListClosedUnscopedAsync(query.UserId, ct);
        var decided = closed
            .Where(r => r.Status is RecommendationStatus.Confirmed or RecommendationStatus.Rejected && r.DecidedAt is not null)
            .ToList();

        var contestants = new List<ScoreboardContestantDto>
        {
            Contestant(
                LedgerAsProposed, null, "Ledger as proposed", decided,
                decided.Select(r => GradeProposal(r, RecommendationValue.ProposesExposure(r.Action))).ToList()),
            Contestant(
                OwnerAsDecided, null, "Owner as decided", decided,
                decided.Select(r => RecommendationValue.KeptExposure(r) is bool kept
                    ? GradeProposal(r, kept)
                    : Graded.Never(r.DecidedAt!.Value)).ToList()),
        };

        var picks = await candidates.ListAllUnscopedAsync(query.UserId, ct);
        var journal = (await events.ListUnscopedAsync(query.UserId, subjectId: null, ct))
            .Where(e => e.SubjectType == ThesisSubjectType.Candidate)
            .GroupBy(e => e.SubjectId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var source in PickSources)
        {
            var rows = picks
                .Where(c => c.Source == source)
                .Select(c => GradePick(c, journal.GetValueOrDefault(c.Id) ?? [], query.AsOf))
                .ToList();
            contestants.Add(Contestant(PaperPicks, source.ToString(), $"Paper picks ({source})", rows.Count, null, rows));
        }

        var undecided = closed
            .Where(r => r.Status is RecommendationStatus.Expired or RecommendationStatus.Superseded)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new UndecidedProposalDto(r.Id, r.Ticker, r.Action.ToString(), r.Status.ToString(), r.CreatedAt))
            .ToList();

        return new LedgerScoreboardDto(
            Benchmark,
            ThesisEventRecorder.DefaultBenchmarkTicker,
            ScoreRecommendationOutcomesCommandHandler.HorizonDays,
            GetTrackRecordQueryHandler.MinimumClosedSampleSize,
            contestants,
            undecided);
    }

    /// <summary>
    /// A decided proposal graded on one side: <paramref name="keptExposure"/> holds the name against the S&amp;P 500, or
    /// holds the S&amp;P 500 against the name for a reduction - so the gross excess is the signed decision value.
    /// </summary>
    private Graded GradeProposal(Recommendation r, bool keptExposure)
    {
        var decidedAt = r.DecidedAt!.Value;
        if (r.OutcomeScoredAt is not { } scoredAt)
        {
            return Graded.Awaiting(decidedAt);
        }

        var chosen = keptExposure ? r.SubjectReturnPct : r.BenchmarkReturnPct;
        var other = keptExposure ? r.BenchmarkReturnPct : r.SubjectReturnPct;
        var result = calculator.Calculate(new ThesisPerformanceInput(
            r.Id,
            r.Status == RecommendationStatus.Confirmed ? ThesisEventType.Confirmed : ThesisEventType.Rejected,
            decidedAt,
            SyntheticBasePrice,
            SyntheticBasePrice,
            ThesisEventType.Snapshot,
            scoredAt,
            Synthetic(chosen),
            Synthetic(other),
            r.Ticker,
            frictionOptions.Value));

        return Graded.Of(result, r.Id, r.Ticker, r.Action.ToString(), decidedAt, scoredAt);
    }

    private Graded GradePick(OpportunityCandidate candidate, IReadOnlyList<ThesisEvent> journal, DateTimeOffset asOf)
    {
        var entry = PaperPickHorizon.Entry(journal);
        if (entry is null)
        {
            return Graded.Never(candidate.CreatedAt);
        }

        var horizon = PaperPickHorizon.HorizonPoint(candidate, journal);
        if (horizon is null)
        {
            return asOf <= PaperPickHorizon.Deadline(candidate)
                ? Graded.Awaiting(candidate.CreatedAt)
                : Graded.Never(candidate.CreatedAt);
        }

        if (entry.PricesPending || horizon.PricesPending)
        {
            // The weekly snapshot job backfills a pending stamp; until then the pick is not yet gradable.
            return Graded.Awaiting(candidate.CreatedAt);
        }

        var result = calculator.Calculate(new ThesisPerformanceInput(
            candidate.Id,
            ThesisEventType.Created,
            entry.Timestamp,
            entry.SubjectPrice,
            entry.BenchmarkPrice,
            horizon.EventType,
            horizon.Timestamp,
            horizon.SubjectPrice,
            horizon.BenchmarkPrice,
            candidate.Ticker,
            frictionOptions.Value));

        return Graded.Of(result, candidate.Id, candidate.Ticker, PickKind, entry.Timestamp, horizon.Timestamp);
    }

    private static ScoreboardContestantDto Contestant(
        string contestant, string? source, string label, IReadOnlyList<Recommendation> decided, IReadOnlyList<Graded> rows)
        => Contestant(
            contestant, source, label, decided.Count,
            decided.Count(r => r.Execution == RecommendationExecution.Executed), rows);

    private static ScoreboardContestantDto Contestant(
        string contestant, string? source, string label, int count, int? executed, IReadOnlyList<Graded> rows)
    {
        var scored = rows.Where(r => r.State == GradeState.Scored).ToList();
        var tooFew = scored.Count < GetTrackRecordQueryHandler.MinimumClosedSampleSize;
        return new ScoreboardContestantDto(
            contestant,
            source,
            label,
            count,
            executed,
            scored.Count,
            rows.Count(r => r.State == GradeState.Awaiting),
            rows.Count(r => r.State == GradeState.Never),
            rows.Count > 0 ? rows.Min(r => r.From) : null,
            Figures(scored, r => r.Gross!.Value),
            Figures(scored, r => r.Net!.Value),
            tooFew,
            tooFew
                ? $"too few to tell skill from luck ({scored.Count} of {GetTrackRecordQueryHandler.MinimumClosedSampleSize})"
                : null);
    }

    private static ScoreboardFiguresDto Figures(IReadOnlyList<Graded> scored, Func<Graded, decimal> excess)
    {
        if (scored.Count == 0)
        {
            return new ScoreboardFiguresDto(null, null, null, null);
        }

        var values = scored.Select(excess).Order().ToList();
        var mid = values.Count / 2;
        var median = values.Count % 2 == 0 ? (values[mid - 1] + values[mid]) / 2m : values[mid];
        var worst = scored.MinBy(excess)!;

        return new ScoreboardFiguresDto(
            Round(values.Average()),
            Round(median),
            Round((decimal)values.Count(v => v > 0m) / values.Count * Percent),
            new ScoreboardRowDto(worst.Id, worst.Ticker, worst.Kind, worst.From, worst.To!.Value, Round(excess(worst))));
    }

    private static decimal? Synthetic(decimal? returnPct)
        => returnPct is decimal r ? SyntheticBasePrice * (1m + r / Percent) : null;

    private static decimal Round(decimal value) => Math.Round(value, FigureDecimals);

    private enum GradeState
    {
        Scored,
        Awaiting,
        Never,
    }

    private sealed record Graded(
        GradeState State, Guid Id, string Ticker, string Kind, DateTimeOffset From, DateTimeOffset? To,
        decimal? Gross, decimal? Net)
    {
        public static Graded Awaiting(DateTimeOffset from) => new(GradeState.Awaiting, Guid.Empty, string.Empty, string.Empty, from, null, null, null);

        public static Graded Never(DateTimeOffset from) => new(GradeState.Never, Guid.Empty, string.Empty, string.Empty, from, null, null, null);

        public static Graded Of(
            ThesisPerformanceResult result, Guid id, string ticker, string kind, DateTimeOffset from, DateTimeOffset to)
            => result.IsEvaluable
                ? new(GradeState.Scored, id, ticker, kind, from, to, result.ExcessReturnPct, result.NetExcessReturnPct)
                : Never(from);
    }
}
