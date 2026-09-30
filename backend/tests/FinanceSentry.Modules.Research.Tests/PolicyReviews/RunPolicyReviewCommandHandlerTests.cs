namespace FinanceSentry.Modules.Research.Tests.PolicyReviews;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.PolicyReviews;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// The scheduled policy review (#696): opens only when the recorded cadence says it is due, records
/// the review, presents the proposal, and reports a missed review — and has no path to an order.
/// </summary>
public class RunPolicyReviewCommandHandlerTests
{
    private static readonly DateTimeOffset LastReview = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset QuarterlyDue = LastReview.AddMonths(3);

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IIpsRepository> _ipsRepo = new();
    private readonly Mock<IPolicyReviewRepository> _reviewRepo = new();
    private readonly Mock<IQueryHandler<GetAllocationDriftQuery, AllocationDriftDto>> _drift = new();

    // Strict: any alert other than the two review alerts — a rebalance ticket, say — fails the test.
    private readonly Mock<IAlertGeneratorService> _alerts = new(MockBehavior.Strict);
    private readonly List<PolicyReview> _recorded = [];

    public RunPolicyReviewCommandHandlerTests()
    {
        _reviewRepo.Setup(r => r.RecordAsync(It.IsAny<PolicyReview>(), It.IsAny<CancellationToken>()))
            .Callback<PolicyReview, CancellationToken>((r, _) => _recorded.Add(r))
            .Returns(Task.CompletedTask);
        _alerts.Setup(a => a.GeneratePolicyReviewAlertAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _alerts.Setup(a => a.GeneratePolicyReviewMissedAlertAsync(
                It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _drift.Setup(d => d.Handle(new GetAllocationDriftQuery(_userId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AllocationDriftDto(
                true, 100_000m, 0m, 100_000m, true,
                [new AllocationSleeveDrift("Equity", 60, 55, 65, 70, 70_000, 10, "OverBand")],
                "annual"));
    }

    [Fact]
    public async Task No_policy_statement_opens_no_review()
    {
        GivenVersions();

        var result = await Handler(QuarterlyDue).Handle(new RunPolicyReviewCommand(_userId), default);

        result.Outcome.Should().Be(PolicyReviewRunOutcome.NoPolicy);
        _recorded.Should().BeEmpty();
        _drift.Verify(d => d.Handle(It.IsAny<GetAllocationDriftQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Review_not_yet_due_opens_nothing()
    {
        GivenVersions(Current());

        var result = await Handler(QuarterlyDue.AddDays(-1)).Handle(new RunPolicyReviewCommand(_userId), default);

        result.Outcome.Should().Be(PolicyReviewRunOutcome.NotDue);
        result.Schedule!.DueAt.Should().Be(QuarterlyDue);
        _recorded.Should().BeEmpty();
        _alerts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Due_review_is_recorded_with_its_proposal_and_presented()
    {
        var ips = Current();
        GivenVersions(ips);
        var now = QuarterlyDue.AddHours(5);

        var result = await Handler(now).Handle(new RunPolicyReviewCommand(_userId), default);

        result.Outcome.Should().Be(PolicyReviewRunOutcome.Completed);
        result.AdjustmentCount.Should().Be(1);
        var review = _recorded.Should().ContainSingle().Subject;
        review.Id.Should().Be(result.ReviewId!.Value);
        review.UserId.Should().Be(_userId);
        review.PolicyStatementId.Should().Be(ips.Id);
        review.PolicyStatementVersion.Should().Be(ips.Version);
        review.ReviewCadence.Should().Be("quarterly");
        review.DueAt.Should().Be(QuarterlyDue);
        review.CompletedAt.Should().Be(now, "completion time becomes the statement's last-reviewed timestamp");
        review.WasMissed.Should().BeFalse();
        review.TotalValueUsd.Should().Be(100_000m);
        review.Sleeves.Should().ContainSingle(s => s.AssetClass == "Equity" && s.Status == "OverBand");
        review.Adjustments.Should().ContainSingle(a => a.Action == PolicyReviewAdjustmentAction.Trim && a.ApproxAmountUsd == 10_000m);
        review.Rationale.Should().Contain(Application.Services.PolicyReviewProposer.RecommendOnlyNote);

        _alerts.Verify(a => a.GeneratePolicyReviewAlertAsync(
            _userId, review.Id, 1, review.Rationale, It.IsAny<CancellationToken>()), Times.Once);
        _alerts.Verify(a => a.GeneratePolicyReviewMissedAlertAsync(
            It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Missed_review_is_reported_and_the_catch_up_review_still_runs()
    {
        GivenVersions(Current());

        var result = await Handler(QuarterlyDue.AddDays(12)).Handle(new RunPolicyReviewCommand(_userId), default);

        result.Outcome.Should().Be(PolicyReviewRunOutcome.Completed);
        _alerts.Verify(a => a.GeneratePolicyReviewMissedAlertAsync(
            _userId, QuarterlyDue, 12, "quarterly", It.IsAny<CancellationToken>()), Times.Once);
        var review = _recorded.Should().ContainSingle().Subject;
        review.WasMissed.Should().BeTrue();
        review.DaysOverdue.Should().Be(12);
    }

    [Fact]
    public async Task Missed_review_is_reported_even_when_the_catch_up_review_fails()
    {
        GivenVersions(Current());
        _drift.Setup(d => d.Handle(It.IsAny<GetAllocationDriftQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("book unavailable"));

        var act = () => Handler(QuarterlyDue.AddDays(12)).Handle(new RunPolicyReviewCommand(_userId), default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _alerts.Verify(a => a.GeneratePolicyReviewMissedAlertAsync(
            _userId, QuarterlyDue, 12, "quarterly", It.IsAny<CancellationToken>()), Times.Once);
        _recorded.Should().BeEmpty("a failed review must not advance the schedule");
    }

    [Fact]
    public async Task Long_rationale_is_clipped_to_the_alert_message_limit_but_kept_whole_on_the_review()
    {
        GivenVersions(Current());
        var sleeves = Enumerable.Range(0, 40)
            .Select(i => new AllocationSleeveDrift($"Sleeve-with-a-long-name-{i}", 2, 1, 3, 5, 5_000, 3, "OverBand"))
            .ToList();
        _drift.Setup(d => d.Handle(It.IsAny<GetAllocationDriftQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AllocationDriftDto(true, 100_000m, 0m, 100_000m, true, sleeves, "annual"));
        string? summary = null;
        _alerts.Setup(a => a.GeneratePolicyReviewAlertAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, int, string, CancellationToken>((_, _, _, s, _) => summary = s)
            .Returns(Task.CompletedTask);

        await Handler(QuarterlyDue).Handle(new RunPolicyReviewCommand(_userId), default);

        _recorded.Single().Rationale.Length.Should().BeGreaterThan(1000);
        summary!.Length.Should().Be(1000);
    }

    /// <summary>
    /// Recommend-only guard: the review path's dependencies are exactly the policy statement, the
    /// review store, the drift read, the alert path and a clock. Adding anything else — an order,
    /// broker or trading service — fails here and has to be argued for.
    /// </summary>
    [Fact]
    public void Review_path_depends_on_nothing_that_can_place_an_order()
    {
        Type[] allowed =
        [
            typeof(IIpsRepository),
            typeof(IPolicyReviewRepository),
            typeof(IQueryHandler<GetAllocationDriftQuery, AllocationDriftDto>),
            typeof(IAlertGeneratorService),
            typeof(TimeProvider),
            typeof(ILogger<RunPolicyReviewCommandHandler>),
            typeof(ICommandHandler<RunPolicyReviewCommand, PolicyReviewRunResult>),
            typeof(ILogger<PolicyReviewJob>),
        ];

        var dependencies = new[] { typeof(RunPolicyReviewCommandHandler), typeof(PolicyReviewJob) }
            .SelectMany(t => t.GetConstructors())
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType);

        dependencies.Should().OnlyContain(t => allowed.Contains(t));
    }

    private RunPolicyReviewCommandHandler Handler(DateTimeOffset now) => new(
        _ipsRepo.Object,
        _reviewRepo.Object,
        _drift.Object,
        _alerts.Object,
        new FixedTimeProvider(now),
        NullLogger<RunPolicyReviewCommandHandler>.Instance);

    private void GivenVersions(params InvestmentPolicyStatement[] versions)
        => _ipsRepo.Setup(r => r.ListVersionsAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(versions);

    private InvestmentPolicyStatement Current() => new()
    {
        UserId = _userId,
        Version = 2,
        ReviewCadence = "quarterly",
        CreatedAt = LastReview.AddYears(-1),
        LastReviewedAt = LastReview,
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
