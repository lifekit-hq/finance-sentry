namespace FinanceSentry.Modules.Research.Tests.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// The post-event counterpart of the earnings-ahead detector: an earnings date that has passed on a thesis ticker, a trigger
/// proxy or an Active candidate raises the digest-lane review notice, once per (subject, ticker, date).
/// </summary>
public sealed class EarningsPassedJobTests
{
    private readonly Mock<IBankingTotalsReader> _banking = new();
    private readonly Mock<ICryptoHoldingsReader> _crypto = new();
    private readonly Mock<IThesisRepository> _theses = new();
    private readonly Mock<ICandidateRepository> _candidates = new();
    private readonly Mock<IEarningsCalendarService> _calendar = new();
    private readonly Mock<IAlertGeneratorService> _alerts = new();
    private readonly List<ReviewDue> _reviews = [];
    private readonly EarningsPassedJob _job;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly DateTime _nowUtc = new(2026, 11, 13, 6, 30, 0, DateTimeKind.Utc);
    private readonly DateOnly _today = new(2026, 11, 13);

    public EarningsPassedJobTests()
    {
        _job = new EarningsPassedJob(
            _banking.Object, _crypto.Object, _calendar.Object,
            new ReviewDueRaiser(_theses.Object, _candidates.Object, _alerts.Object),
            NullLogger<EarningsPassedJob>.Instance);

        _banking.Setup(b => b.GetActiveUserIdsAsync(default)).ReturnsAsync([_userId]);
        _crypto.Setup(c => c.GetHoldingsAsync(_userId, default)).ReturnsAsync([]);
        _theses.Setup(t => t.ListUnscopedAsync(_userId, default)).ReturnsAsync([]);
        _candidates.Setup(c => c.ListAllUnscopedAsync(_userId, default)).ReturnsAsync([]);
        _alerts.Setup(a => a.GenerateReviewDueAlertAsync(_userId, It.IsAny<ReviewDue>(), default))
            .Callback<Guid, ReviewDue, CancellationToken>((_, review, _) => _reviews.Add(review))
            .Returns(Task.CompletedTask);
    }

    private void CalendarReturns(params EarningsEvent[] events)
        => _calendar.Setup(c => c.GetForTickersAsync(
                It.IsAny<IReadOnlyCollection<string>>(), _today.AddDays(-EarningsPassedJob.LookbackDays), _today.AddDays(-1),
                EarningsEventType.Earnings, default, false))
            .ReturnsAsync(events);

    private static EarningsEvent Earnings(string ticker, DateOnly date, bool estimate = false)
        => new(ticker, EarningsEventType.Earnings, date, estimate, "yahoo");

    [Fact]
    public async Task A_passed_earnings_date_on_a_thesis_ticker_raises_a_digest_lane_thesis_notice()
    {
        var thesis = new InvestmentThesis { Id = Guid.NewGuid(), UserId = _userId, Ticker = "NVDA" };
        _theses.Setup(t => t.ListUnscopedAsync(_userId, default)).ReturnsAsync([thesis]);
        CalendarReturns(Earnings("NVDA", new DateOnly(2026, 11, 12)));

        await _job.ExecuteAsync(_nowUtc);

        var review = _reviews.Should().ContainSingle().Subject;
        review.Subject.Should().Be(ReviewSubjectKind.Thesis);
        review.SubjectId.Should().Be(thesis.Id);
        review.Source.Should().Be(ReviewDueSource.Earnings);
        review.Reference.Should().Be("NVDA:2026-11-12");
        review.Evidence.Should().Equal("earnings:NVDA:2026-11-12");
        review.Headline.Should().Contain("2026-11-12 has passed");
        ReviewDueSource.IsRealtime(review.Source).Should().BeFalse();
    }

    [Fact]
    public async Task Only_dates_that_have_passed_are_asked_for()
    {
        _theses.Setup(t => t.ListUnscopedAsync(_userId, default))
            .ReturnsAsync([new InvestmentThesis { Id = Guid.NewGuid(), UserId = _userId, Ticker = "NVDA" }]);
        CalendarReturns();

        await _job.ExecuteAsync(_nowUtc);

        _calendar.Verify(c => c.GetForTickersAsync(
            It.Is<IReadOnlyCollection<string>>(t => t.Contains("NVDA")),
            new DateOnly(2026, 11, 8), new DateOnly(2026, 11, 12), EarningsEventType.Earnings, default, false), Times.Once);
    }

    [Fact]
    public async Task A_passed_date_on_a_trigger_proxy_notifies_the_thesis_that_names_it()
    {
        var thesis = new InvestmentThesis
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            Ticker = "SOXX",
            InvalidationTriggers = [new ThesisInvalidationTrigger("gross_margin", "lessThan", 0.35m, "MU")],
        };
        _theses.Setup(t => t.ListUnscopedAsync(_userId, default)).ReturnsAsync([thesis]);
        CalendarReturns(Earnings("MU", new DateOnly(2026, 11, 11)));

        await _job.ExecuteAsync(_nowUtc);

        var review = _reviews.Should().ContainSingle().Subject;
        review.SubjectId.Should().Be(thesis.Id);
        review.Ticker.Should().Be("SOXX");
        review.Reference.Should().Be("MU:2026-11-11");
    }

    [Fact]
    public async Task A_passed_date_on_an_Active_candidate_raises_a_candidate_notice()
    {
        var candidate = new OpportunityCandidate { Id = Guid.NewGuid(), Ticker = "CBRS", Status = CandidateStatus.Active };
        _candidates.Setup(c => c.ListAllUnscopedAsync(_userId, default)).ReturnsAsync([candidate]);
        CalendarReturns(Earnings("CBRS", new DateOnly(2026, 11, 10)));

        await _job.ExecuteAsync(_nowUtc);

        var review = _reviews.Should().ContainSingle().Subject;
        review.Subject.Should().Be(ReviewSubjectKind.Candidate);
        review.SubjectId.Should().Be(candidate.Id);
        review.Source.Should().Be(ReviewDueSource.Earnings);
    }

    [Fact]
    public async Task A_date_on_a_name_with_no_thesis_or_candidate_raises_nothing()
    {
        _theses.Setup(t => t.ListUnscopedAsync(_userId, default))
            .ReturnsAsync([new InvestmentThesis { Id = Guid.NewGuid(), UserId = _userId, Ticker = "NVDA" }]);
        CalendarReturns(Earnings("AAPL", new DateOnly(2026, 11, 12)));

        await _job.ExecuteAsync(_nowUtc);

        _reviews.Should().BeEmpty();
    }

    [Fact]
    public async Task No_subjects_means_no_calendar_fetch()
    {
        await _job.ExecuteAsync(_nowUtc);

        _calendar.Verify(c => c.GetForTickersAsync(
            It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string?>(),
            default, It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task A_crypto_asset_thesis_ticker_is_left_out_of_the_calendar_lookup()
    {
        _theses.Setup(t => t.ListUnscopedAsync(_userId, default)).ReturnsAsync([
            new InvestmentThesis { Id = Guid.NewGuid(), UserId = _userId, Ticker = "SOL" },
            new InvestmentThesis { Id = Guid.NewGuid(), UserId = _userId, Ticker = "NVDA" },
        ]);
        _crypto.Setup(c => c.GetHoldingsAsync(_userId, default)).ReturnsAsync([
            new CryptoHoldingSummary("SOL", 10m, 0m, 1500m, DateTime.UtcNow, "Binance"),
        ]);
        CalendarReturns();

        await _job.ExecuteAsync(_nowUtc);

        _calendar.Verify(c => c.GetForTickersAsync(
            It.Is<IReadOnlyCollection<string>>(t => t.Contains("NVDA") && !t.Contains("SOL")),
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), EarningsEventType.Earnings, default, false), Times.Once);
    }

    [Fact]
    public async Task A_failed_calendar_fetch_is_silence_not_a_job_failure()
    {
        _theses.Setup(t => t.ListUnscopedAsync(_userId, default))
            .ReturnsAsync([new InvestmentThesis { Id = Guid.NewGuid(), UserId = _userId, Ticker = "NVDA" }]);
        _calendar.Setup(c => c.GetForTickersAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string?>(),
                default, It.IsAny<bool>()))
            .ThrowsAsync(new HttpRequestException("yahoo down"));

        await _job.ExecuteAsync(_nowUtc);

        _reviews.Should().BeEmpty();
    }

    [Fact]
    public async Task The_next_day_re_reads_the_same_date_and_hands_the_generator_the_same_notice()
    {
        _theses.Setup(t => t.ListUnscopedAsync(_userId, default))
            .ReturnsAsync([new InvestmentThesis { Id = Guid.NewGuid(), UserId = _userId, Ticker = "NVDA" }]);
        var date = new DateOnly(2026, 11, 12);
        CalendarReturns(Earnings("NVDA", date));
        _calendar.Setup(c => c.GetForTickersAsync(
                It.IsAny<IReadOnlyCollection<string>>(), _today.AddDays(-4), _today, EarningsEventType.Earnings, default, false))
            .ReturnsAsync([Earnings("NVDA", date)]);

        await _job.ExecuteAsync(_nowUtc);
        await _job.ExecuteAsync(_nowUtc.AddDays(1));

        _reviews.Should().HaveCount(2);
        _reviews[1].Reference.Should().Be(_reviews[0].Reference);
        _reviews[1].SubjectId.Should().Be(_reviews[0].SubjectId);
    }
}
