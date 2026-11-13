namespace FinanceSentry.Modules.Research.Tests.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>A dated catalyst on a thesis that has passed raises the digest-lane review notice, once per catalyst.</summary>
public sealed class CatalystPassedJobTests
{
    private readonly Mock<IBankingTotalsReader> _banking = new();
    private readonly Mock<IThesisRepository> _theses = new();
    private readonly Mock<ICandidateRepository> _candidates = new();
    private readonly Mock<IAlertGeneratorService> _alerts = new();
    private readonly List<ReviewDue> _reviews = [];
    private readonly CatalystPassedJob _job;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly DateTime _nowUtc = new(2026, 11, 13, 6, 35, 0, DateTimeKind.Utc);

    public CatalystPassedJobTests()
    {
        _job = new CatalystPassedJob(
            _banking.Object, _theses.Object,
            new ReviewDueRaiser(_theses.Object, _candidates.Object, _alerts.Object),
            NullLogger<CatalystPassedJob>.Instance);

        _banking.Setup(b => b.GetActiveUserIdsAsync(default)).ReturnsAsync([_userId]);
        _candidates.Setup(c => c.ListAllUnscopedAsync(_userId, default)).ReturnsAsync([]);
        _alerts.Setup(a => a.GenerateReviewDueAlertAsync(_userId, It.IsAny<ReviewDue>(), default))
            .Callback<Guid, ReviewDue, CancellationToken>((_, review, _) => _reviews.Add(review))
            .Returns(Task.CompletedTask);
    }

    private InvestmentThesis WithCatalysts(params ThesisCatalyst[] catalysts)
    {
        var thesis = new InvestmentThesis { Id = Guid.NewGuid(), UserId = _userId, Ticker = "CBRS", Catalysts = [.. catalysts] };
        _theses.Setup(t => t.ListUnscopedAsync(_userId, default)).ReturnsAsync([thesis]);
        return thesis;
    }

    [Fact]
    public async Task A_catalyst_dated_yesterday_raises_a_digest_lane_thesis_notice()
    {
        var thesis = WithCatalysts(new ThesisCatalyst(new DateOnly(2026, 11, 12), "Q3 print"));

        await _job.ExecuteAsync(_nowUtc);

        var review = _reviews.Should().ContainSingle().Subject;
        review.Subject.Should().Be(ReviewSubjectKind.Thesis);
        review.SubjectId.Should().Be(thesis.Id);
        review.Ticker.Should().Be("CBRS");
        review.Source.Should().Be(ReviewDueSource.Catalyst);
        review.Reference.Should().StartWith("2026-11-12:");
        review.Evidence.Should().Equal("catalyst:2026-11-12");
        review.Headline.Should().Contain("Q3 print");
        ReviewDueSource.IsRealtime(review.Source).Should().BeFalse();
    }

    [Fact]
    public async Task A_catalyst_dated_today_has_not_passed_yet()
    {
        WithCatalysts(new ThesisCatalyst(new DateOnly(2026, 11, 13), "Investor day"));

        await _job.ExecuteAsync(_nowUtc);

        _reviews.Should().BeEmpty();
    }

    [Fact]
    public async Task A_future_catalyst_raises_nothing()
    {
        WithCatalysts(new ThesisCatalyst(new DateOnly(2026, 12, 1), "Investor day"));

        await _job.ExecuteAsync(_nowUtc);

        _reviews.Should().BeEmpty();
    }

    [Fact]
    public async Task A_catalyst_older_than_the_window_is_not_revived()
    {
        WithCatalysts(new ThesisCatalyst(new DateOnly(2026, 11, 13).AddDays(-CatalystPassedJob.LookbackDays - 1), "Old news"));

        await _job.ExecuteAsync(_nowUtc);

        _reviews.Should().BeEmpty();
    }

    [Fact]
    public async Task Two_catalysts_on_one_day_are_two_notices_and_the_same_catalyst_keeps_one_reference_across_runs()
    {
        WithCatalysts(
            new ThesisCatalyst(new DateOnly(2026, 11, 12), "Q3 print"),
            new ThesisCatalyst(new DateOnly(2026, 11, 12), "FDA decision"));

        await _job.ExecuteAsync(_nowUtc);
        await _job.ExecuteAsync(_nowUtc.AddDays(1));

        _reviews.Should().HaveCount(4);
        _reviews.Select(r => r.Reference).Distinct().Should().HaveCount(2);
        _reviews.Take(2).Select(r => r.Reference).Should().BeEquivalentTo(_reviews.Skip(2).Select(r => r.Reference));
    }

    [Fact]
    public async Task A_catalyst_notifies_its_own_thesis_only()
    {
        var thesis = new InvestmentThesis
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            Ticker = "SOXX",
            Catalysts = [new ThesisCatalyst(new DateOnly(2026, 11, 12), "Fab capex update")],
            InvalidationTriggers = [new ThesisInvalidationTrigger("gross_margin", "lessThan", 0.35m, "MU")],
        };
        var other = new InvestmentThesis { Id = Guid.NewGuid(), UserId = _userId, Ticker = "MU" };
        _theses.Setup(t => t.ListUnscopedAsync(_userId, default)).ReturnsAsync([thesis, other]);

        await _job.ExecuteAsync(_nowUtc);

        _reviews.Should().ContainSingle().Which.SubjectId.Should().Be(thesis.Id);
    }

    [Fact]
    public async Task A_thesis_with_no_catalysts_raises_nothing()
    {
        WithCatalysts();

        await _job.ExecuteAsync(_nowUtc);

        _reviews.Should().BeEmpty();
    }
}
