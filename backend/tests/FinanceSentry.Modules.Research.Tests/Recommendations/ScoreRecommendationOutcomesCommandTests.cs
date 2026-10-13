namespace FinanceSentry.Modules.Research.Tests.Recommendations;

using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Recommendations;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public sealed class ScoreRecommendationOutcomesCommandTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateTimeOffset DecidedAt = new(2026, 9, 1, 14, 0, 0, TimeSpan.Zero);

    private readonly InMemoryRecommendationRepository _repo = new();
    private readonly Mock<IThesisEventRepository> _events = new();
    private readonly Mock<IPositionLedgerSource> _ledger = new();

    private ScoreRecommendationOutcomesCommandHandler Handler()
        => new(_repo, _events.Object, _ledger.Object,
            new FixedQuotes(new Dictionary<string, decimal> { ["GRAB"] = 2.5m, ["SPY"] = 550m }),
            NullLogger<ScoreRecommendationOutcomesCommandHandler>.Instance);

    private Recommendation Decided(RecommendationStatus status = RecommendationStatus.Confirmed, RecommendationAction action = RecommendationAction.Trim)
    {
        var r = new Recommendation
        {
            UserId = User,
            Ticker = "GRAB",
            Action = action,
            Status = status,
            DecidedAt = DecidedAt,
            Execution = status == RecommendationStatus.Confirmed ? RecommendationExecution.Pending : RecommendationExecution.NotApplicable,
        };
        _repo.Rows.Add(r);
        _events.Setup(e => e.ListUnscopedAsync(User, r.Id, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new ThesisEvent
            {
                UserId = User, SubjectType = ThesisSubjectType.Recommendation, SubjectId = r.Id, Ticker = "GRAB",
                EventType = status == RecommendationStatus.Confirmed ? ThesisEventType.Confirmed : ThesisEventType.Rejected,
                Timestamp = DecidedAt, SubjectPrice = 3.125m, BenchmarkPrice = 500m,
            },
        ]);
        return r;
    }

    private void Fills(params (DateTime At, decimal Qty)[] fills)
        => _ledger.Setup(l => l.ReadForSymbolAsync(User, "GRAB", It.IsAny<CancellationToken>())).ReturnsAsync(new PositionLedger(
            fills.Select(f => new LedgerFill("ibkr", f.At, f.Qty, 3.1m, null, null, "USD")).ToList(), []));

    [Fact]
    public async Task A_sell_fill_after_a_confirmed_trim_marks_it_executed_and_it_is_scored_against_the_sp500()
    {
        var r = Decided();
        Fills((DecidedAt.UtcDateTime.AddDays(2), -656m));

        await Handler().Handle(new ScoreRecommendationOutcomesCommand(DecidedAt.AddDays(3)), CancellationToken.None);

        r.Execution.Should().Be(RecommendationExecution.Executed);
        r.ExecutedAt.Should().Be(DecidedAt.AddDays(2));
        r.SubjectReturnPct.Should().Be(-20m);
        r.BenchmarkReturnPct.Should().Be(10m);
        r.OutcomeScoredAt.Should().BeNull("the 30-day horizon has not passed");
    }

    [Fact]
    public async Task A_buy_fill_does_not_execute_a_trim()
    {
        var r = Decided();
        Fills((DecidedAt.UtcDateTime.AddDays(1), 100m));

        await Handler().Handle(new ScoreRecommendationOutcomesCommand(DecidedAt.AddDays(3)), CancellationToken.None);

        r.Execution.Should().Be(RecommendationExecution.Pending);
    }

    [Fact]
    public async Task A_fill_stamped_the_night_before_the_decision_still_counts()
    {
        var r = Decided(action: RecommendationAction.Add);
        Fills((DecidedAt.UtcDateTime.Date, 50m));

        await Handler().Handle(new ScoreRecommendationOutcomesCommand(DecidedAt.AddDays(1)), CancellationToken.None);

        r.Execution.Should().Be(RecommendationExecution.Executed);
    }

    [Fact]
    public async Task No_fill_by_the_end_of_the_window_is_unexecuted_and_scoring_closes_at_the_horizon()
    {
        var r = Decided();
        Fills();

        await Handler().Handle(new ScoreRecommendationOutcomesCommand(DecidedAt.AddDays(ScoreRecommendationOutcomesCommandHandler.HorizonDays)), CancellationToken.None);

        r.Execution.Should().Be(RecommendationExecution.Unexecuted);
        r.OutcomeScoredAt.Should().Be(DecidedAt.AddDays(ScoreRecommendationOutcomesCommandHandler.HorizonDays));
        (await _repo.ListOutcomeOpenUnscopedAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_rejected_proposal_is_scored_as_the_counterfactual_without_watching_for_a_fill()
    {
        var r = Decided(RecommendationStatus.Rejected);

        await Handler().Handle(new ScoreRecommendationOutcomesCommand(DecidedAt.AddDays(31)), CancellationToken.None);

        r.Execution.Should().Be(RecommendationExecution.NotApplicable);
        r.SubjectReturnPct.Should().Be(-20m);
        r.OutcomeScoredAt.Should().NotBeNull();
        _ledger.Verify(l => l.ReadForSymbolAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
