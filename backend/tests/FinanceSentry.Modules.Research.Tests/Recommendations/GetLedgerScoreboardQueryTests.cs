namespace FinanceSentry.Modules.Research.Tests.Recommendations;

using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Recommendations;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Tests.Opportunity;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>#986: the Ledger scoreboard, an arena of one, graded against the S&amp;P 500.</summary>
public sealed class GetLedgerScoreboardQueryTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateTimeOffset AsOf = new(2026, 10, 19, 6, 0, 0, TimeSpan.Zero);

    private readonly InMemoryRecommendationRepository _recommendations = new();
    private readonly FakeCandidateRepository _candidates = new();
    private readonly List<ThesisEvent> _journal = [];

    private async Task<LedgerScoreboardDto> Board()
    {
        var events = new Mock<IThesisEventRepository>();
        events.Setup(e => e.ListUnscopedAsync(User, null, It.IsAny<CancellationToken>())).ReturnsAsync(() => _journal);
        var handler = new GetLedgerScoreboardQueryHandler(
            _recommendations, _candidates, events.Object, new ThesisPerformanceCalculator(), Options.Create(new FrictionConfig()));
        return await handler.Handle(new GetLedgerScoreboardQuery(User, AsOf), CancellationToken.None);
    }

    private static ScoreboardContestantDto Contestant(LedgerScoreboardDto board, string contestant, string? source = null)
        => board.Contestants.Single(c => c.Contestant == contestant && c.Source == source);

    private Recommendation Decided(
        RecommendationAction action,
        RecommendationStatus status,
        decimal? subjectReturnPct,
        decimal? benchmarkReturnPct,
        RecommendationExecution? execution = null,
        int daysAgo = 40)
    {
        var decidedAt = AsOf.AddDays(-daysAgo);
        var r = new Recommendation
        {
            UserId = User,
            Ticker = $"T{_recommendations.Rows.Count}",
            Action = action,
            Status = status,
            CreatedAt = decidedAt.AddDays(-1),
            DecidedAt = decidedAt,
            Execution = execution ?? (status == RecommendationStatus.Confirmed ? RecommendationExecution.Executed : RecommendationExecution.NotApplicable),
            SubjectReturnPct = subjectReturnPct,
            BenchmarkReturnPct = benchmarkReturnPct,
            OutcomeScoredAt = subjectReturnPct is null ? null : decidedAt.AddDays(30),
        };
        _recommendations.Rows.Add(r);
        return r;
    }

    [Fact]
    public async Task Rejected_rows_count_for_both_contestants_signed_by_each_side()
    {
        Decided(RecommendationAction.Add, RecommendationStatus.Confirmed, 5m, 1m);
        var rejectedAdd = Decided(RecommendationAction.Add, RecommendationStatus.Rejected, 6m, 1m);
        Decided(RecommendationAction.Hold, RecommendationStatus.Rejected, 3m, 1m);
        var unplacedTrim = Decided(RecommendationAction.Trim, RecommendationStatus.Confirmed, 2m, 1m, RecommendationExecution.Unexecuted);
        Decided(RecommendationAction.Exit, RecommendationStatus.Confirmed, null, null, daysAgo: 5);

        var board = await Board();

        var ledger = Contestant(board, GetLedgerScoreboardQueryHandler.LedgerAsProposed);
        ledger.Decided.Should().Be(5);
        ledger.Executed.Should().Be(2);
        ledger.Scored.Should().Be(4);
        ledger.AwaitingHorizon.Should().Be(1);
        ledger.NotScored.Should().Be(0);
        ledger.WindowStart.Should().Be(AsOf.AddDays(-40));
        ledger.Gross.MeanExcessPct.Should().Be(2.5m, "the proposals as made: +4, +5, +2 and -1");
        ledger.Gross.MedianExcessPct.Should().Be(3m);
        ledger.Gross.BeatBenchmarkSharePct.Should().Be(75m);
        ledger.Gross.Worst!.Id.Should().Be(unplacedTrim.Id);
        ledger.Gross.Worst.ExcessPct.Should().Be(-1m);

        var owner = Contestant(board, GetLedgerScoreboardQueryHandler.OwnerAsDecided);
        owner.Decided.Should().Be(5);
        owner.Scored.Should().Be(2, "a rejected Hold and an order never placed have no side to grade");
        owner.AwaitingHorizon.Should().Be(1);
        owner.NotScored.Should().Be(2);
        owner.Gross.MeanExcessPct.Should().Be(-0.5m, "the owner's decision values: +4 and -5");
        owner.Gross.BeatBenchmarkSharePct.Should().Be(50m);
        owner.Gross.Worst!.Id.Should().Be(rejectedAdd.Id);
        owner.Gross.Worst.ExcessPct.Should().Be(RecommendationValue.DecisionValuePct(rejectedAdd));
    }

    [Fact]
    public async Task Expired_and_superseded_proposals_are_listed_but_never_scored()
    {
        Decided(RecommendationAction.Add, RecommendationStatus.Confirmed, 5m, 1m);
        var expired = new Recommendation
        {
            UserId = User, Ticker = "OLD", Action = RecommendationAction.Exit, Status = RecommendationStatus.Expired,
            CreatedAt = AsOf.AddDays(-20), SubjectReturnPct = 9m, BenchmarkReturnPct = 1m,
        };
        var superseded = new Recommendation
        {
            UserId = User, Ticker = "NEW", Action = RecommendationAction.Trim, Status = RecommendationStatus.Superseded,
            CreatedAt = AsOf.AddDays(-10),
        };
        _recommendations.Rows.AddRange([expired, superseded]);

        var board = await Board();

        board.EndedWithoutDecision.Select(u => (u.Id, u.Status))
            .Should().Equal((superseded.Id, "Superseded"), (expired.Id, "Expired"));
        Contestant(board, GetLedgerScoreboardQueryHandler.LedgerAsProposed).Decided.Should().Be(1);
        Contestant(board, GetLedgerScoreboardQueryHandler.OwnerAsDecided).Scored.Should().Be(1);
    }

    [Fact]
    public async Task Net_figures_apply_spec_020_friction_to_the_side_the_decision_chose()
    {
        // A confirmed Exit: the owner held the S&P 500 (+2) instead of the name (-4).
        Decided(RecommendationAction.Exit, RecommendationStatus.Confirmed, -4m, 2m);

        var board = await Board();

        var owner = Contestant(board, GetLedgerScoreboardQueryHandler.OwnerAsDecided);
        owner.Gross.MeanExcessPct.Should().Be(6m);
        // (2 - 0.10 cost) x (1 - 0.37 short-term tax) - (-4)
        owner.Net.MeanExcessPct.Should().Be(5.197m);
        owner.Net.Worst!.ExcessPct.Should().Be(5.197m);
    }

    [Fact]
    public async Task Below_the_minimum_sample_it_reads_too_few_to_tell_counting_decisions_not_days()
    {
        Decided(RecommendationAction.Add, RecommendationStatus.Confirmed, 5m, 1m);

        var board = await Board();

        board.Benchmark.Should().Be("S&P 500");
        board.BenchmarkTicker.Should().Be("SPY");
        board.HorizonDays.Should().Be(30);
        board.MinimumSampleSize.Should().Be(GetTrackRecordQueryHandler.MinimumClosedSampleSize);
        var ledger = Contestant(board, GetLedgerScoreboardQueryHandler.LedgerAsProposed);
        ledger.TooFewToTell.Should().BeTrue();
        ledger.Caveat.Should().Be("too few to tell skill from luck (1 of 30)");
    }

    [Fact]
    public async Task At_the_minimum_sample_the_caveat_drops()
    {
        for (var i = 0; i < GetTrackRecordQueryHandler.MinimumClosedSampleSize; i++)
        {
            Decided(RecommendationAction.Add, RecommendationStatus.Confirmed, i % 2 == 0 ? 3m : -1m, 1m, daysAgo: 40 + i);
        }

        var board = await Board();

        var ledger = Contestant(board, GetLedgerScoreboardQueryHandler.LedgerAsProposed);
        ledger.Scored.Should().Be(30);
        ledger.TooFewToTell.Should().BeFalse();
        ledger.Caveat.Should().BeNull();
        ledger.Gross.MeanExcessPct.Should().Be(0m);
        ledger.Gross.MedianExcessPct.Should().Be(0m);
        ledger.Gross.BeatBenchmarkSharePct.Should().Be(50m);
        ledger.WindowStart.Should().Be(AsOf.AddDays(-69));
    }

    [Fact]
    public async Task With_nothing_decided_every_contestant_reads_empty()
    {
        var board = await Board();

        board.Contestants.Select(c => (c.Contestant, c.Source)).Should().Equal(
            (GetLedgerScoreboardQueryHandler.LedgerAsProposed, null),
            (GetLedgerScoreboardQueryHandler.OwnerAsDecided, null),
            (GetLedgerScoreboardQueryHandler.PaperPicks, "User"),
            (GetLedgerScoreboardQueryHandler.PaperPicks, "Ledger"),
            (GetLedgerScoreboardQueryHandler.PaperPicks, "Scan"));
        board.Contestants.Should().AllSatisfy(c =>
        {
            c.Scored.Should().Be(0);
            c.WindowStart.Should().BeNull();
            c.Gross.Should().Be(new ScoreboardFiguresDto(null, null, null, null));
            c.Caveat.Should().Be("too few to tell skill from luck (0 of 30)");
        });
    }

    private OpportunityCandidate Pick(CandidateSource source, int daysAgo)
    {
        var c = new OpportunityCandidate
        {
            UserId = User, Ticker = $"P{_candidates.Candidates.Count}", Source = source, CreatedAt = AsOf.AddDays(-daysAgo),
        };
        _candidates.Candidates.Add(c);
        return c;
    }

    private void Stamp(OpportunityCandidate c, ThesisEventType type, int afterDays, decimal? subject, decimal? benchmark, bool pending = false)
        => _journal.Add(new ThesisEvent
        {
            UserId = User, SubjectType = ThesisSubjectType.Candidate, SubjectId = c.Id, Ticker = c.Ticker, EventType = type,
            Timestamp = c.CreatedAt.AddDays(afterDays), SubjectPrice = subject, BenchmarkPrice = benchmark, PricesPending = pending,
        });

    [Fact]
    public async Task Paper_picks_are_graded_per_source_from_the_created_stamp_to_the_horizon_stamp()
    {
        var graded = Pick(CandidateSource.User, 45);
        Stamp(graded, ThesisEventType.Created, 0, 100m, 500m);
        Stamp(graded, ThesisEventType.Snapshot, 7, 150m, 500m);
        Stamp(graded, ThesisEventType.Snapshot, 31, 110m, 505m);

        var pendingPrices = Pick(CandidateSource.User, 40);
        Stamp(pendingPrices, ThesisEventType.Created, 0, 100m, 500m);
        Stamp(pendingPrices, ThesisEventType.Expired, 30, null, null, pending: true);

        var inWindow = Pick(CandidateSource.Ledger, 10);
        Stamp(inWindow, ThesisEventType.Created, 0, 100m, 500m);

        var missedHorizon = Pick(CandidateSource.Scan, 60);
        Stamp(missedHorizon, ThesisEventType.Created, 0, 100m, 500m);
        Stamp(missedHorizon, ThesisEventType.Snapshot, 50, 120m, 500m);

        Pick(CandidateSource.Scan, 50);

        var board = await Board();

        var user = Contestant(board, GetLedgerScoreboardQueryHandler.PaperPicks, "User");
        user.Decided.Should().Be(2);
        user.Executed.Should().BeNull("paper picks never trade");
        user.Scored.Should().Be(1);
        user.AwaitingHorizon.Should().Be(1, "the weekly snapshot job has not priced its horizon yet");
        user.WindowStart.Should().Be(graded.CreatedAt);
        user.Gross.MeanExcessPct.Should().Be(9m, "+10% for the name against +1% for the S&P 500 at the horizon, not the week-one stamp");
        user.Gross.Worst!.Kind.Should().Be("Pick");
        user.Gross.Worst.To.Should().Be(graded.CreatedAt.AddDays(31));

        var ledger = Contestant(board, GetLedgerScoreboardQueryHandler.PaperPicks, "Ledger");
        ledger.Decided.Should().Be(1);
        ledger.AwaitingHorizon.Should().Be(1);

        var scan = Contestant(board, GetLedgerScoreboardQueryHandler.PaperPicks, "Scan");
        scan.Decided.Should().Be(2);
        scan.Scored.Should().Be(0);
        scan.NotScored.Should().Be(2, "a horizon stamped past the grace window and a pick with no entry stamp are never graded");
        scan.Gross.MeanExcessPct.Should().BeNull();
    }
}
