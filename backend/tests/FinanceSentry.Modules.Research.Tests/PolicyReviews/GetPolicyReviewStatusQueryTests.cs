namespace FinanceSentry.Modules.Research.Tests.PolicyReviews;

using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.PolicyReviews;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>The read side of the scheduled policy review: a missed review is visible on demand.</summary>
public class GetPolicyReviewStatusQueryTests
{
    private static readonly DateTimeOffset LastReview = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IIpsRepository> _ipsRepo = new();
    private readonly Mock<IPolicyReviewRepository> _reviewRepo = new();

    [Fact]
    public async Task No_policy_statement_reports_no_schedule()
    {
        _ipsRepo.Setup(r => r.ListVersionsAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var status = await Handler(LastReview).Handle(new GetPolicyReviewStatusQuery(_userId), default);

        status.HasIps.Should().BeFalse();
        status.NextDueAt.Should().BeNull();
        status.LatestReview.Should().BeNull();
    }

    [Fact]
    public async Task Overdue_review_is_reported_as_missed_with_the_latest_proposal()
    {
        _ipsRepo.Setup(r => r.ListVersionsAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new InvestmentPolicyStatement
            {
                UserId = _userId, ReviewCadence = "quarterly", CreatedAt = LastReview.AddYears(-1), LastReviewedAt = LastReview,
            },
        ]);
        var latest = new PolicyReview
        {
            UserId = _userId,
            CompletedAt = LastReview,
            Adjustments = [new PolicyReviewAdjustment("Equity", PolicyReviewAdjustmentAction.Trim, 10_000, "Above its band.")],
            Rationale = "Scheduled quarterly review.",
        };
        _reviewRepo.Setup(r => r.GetLatestAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(latest);

        var status = await Handler(LastReview.AddMonths(3).AddDays(20)).Handle(new GetPolicyReviewStatusQuery(_userId), default);

        status.HasIps.Should().BeTrue();
        status.Cadence.Should().Be("quarterly");
        status.LastReviewedAt.Should().Be(LastReview);
        status.NextDueAt.Should().Be(LastReview.AddMonths(3));
        status.IsDue.Should().BeTrue();
        status.IsMissed.Should().BeTrue();
        status.DaysOverdue.Should().Be(20);
        status.LatestReview!.Id.Should().Be(latest.Id);
        status.LatestReview.Adjustments.Should().ContainSingle(a => a.Action == PolicyReviewAdjustmentAction.Trim);
    }

    private GetPolicyReviewStatusQueryHandler Handler(DateTimeOffset now)
        => new(_ipsRepo.Object, _reviewRepo.Object, new FixedTimeProvider(now));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
