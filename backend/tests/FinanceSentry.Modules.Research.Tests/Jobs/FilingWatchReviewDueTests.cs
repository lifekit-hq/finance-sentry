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
/// A filing on a thesis ticker or one of its trigger proxies, or on an Active candidate, raises the realtime review notice
/// for that subject alongside (never instead of) the FilingLanded alert; a held name with neither a thesis nor a candidate
/// raises none.
/// </summary>
public sealed class FilingWatchReviewDueTests
{
    private readonly Mock<IBankingTotalsReader> _banking = new();
    private readonly Mock<IBrokerageHoldingsReader> _brokerage = new();
    private readonly Mock<IThesisRepository> _theses = new();
    private readonly Mock<ICandidateRepository> _candidates = new();
    private readonly Mock<IWatchlistReader> _watchlist = new();
    private readonly Mock<ICryptoHoldingsReader> _crypto = new();
    private readonly Mock<ISecEdgarService> _secEdgar = new();
    private readonly Mock<IAlertGeneratorService> _alerts = new();
    private readonly List<ReviewDue> _reviews = [];
    private readonly FilingWatchJob _job;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly DateTime _nowUtc = new(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);
    private readonly DateOnly _today = new(2026, 9, 21);

    public FilingWatchReviewDueTests()
    {
        _job = new FilingWatchJob(
            _banking.Object,
            new LookaheadUniverse(_brokerage.Object, _watchlist.Object, _theses.Object, _crypto.Object, CoverageStubs.NoMembers()),
            _secEdgar.Object,
            _alerts.Object,
            new ReviewDueRaiser(_theses.Object, _candidates.Object, _alerts.Object, CoverageStubs.NoReviewer()),
            NullLogger<FilingWatchJob>.Instance);

        _banking.Setup(b => b.GetActiveUserIdsAsync(default)).ReturnsAsync([_userId]);
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default)).ReturnsAsync([]);
        _theses.Setup(t => t.ListUnscopedAsync(_userId, default)).ReturnsAsync([]);
        _candidates.Setup(c => c.ListAllUnscopedAsync(_userId, default)).ReturnsAsync([]);
        _alerts.Setup(a => a.GenerateReviewDueAlertAsync(_userId, It.IsAny<ReviewDue>(), default))
            .Callback<Guid, ReviewDue, CancellationToken>((_, review, _) => _reviews.Add(review))
            .Returns(Task.CompletedTask);
    }

    private EdgarFiling Filing(string ticker, string form = "10-Q", DateOnly? date = null, string accession = "0001-26-000123")
        => new(ticker, form, date ?? _today, null, $"{form} description", accession,
            $"https://www.sec.gov/Archives/edgar/data/1/{accession}/doc.htm", true);

    private void FilingsFor(string ticker, params EdgarFiling[] filings)
        => _secEdgar.Setup(e => e.GetRecentFilingsAsync(
                ticker, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), default))
            .ReturnsAsync(filings);

    private static InvestmentThesis Thesis(string ticker, string? proxy = null) => new()
    {
        Id = Guid.NewGuid(),
        Ticker = ticker,
        InvalidationTriggers = proxy is null ? [] : [new ThesisInvalidationTrigger("gross_margin", "lessThan", 0.35m, proxy)],
    };

    private static OpportunityCandidate Candidate(string ticker, CandidateStatus status = CandidateStatus.Active) => new()
    {
        Id = Guid.NewGuid(),
        Ticker = ticker,
        Status = status,
    };

    [Fact]
    public async Task A_filing_on_a_thesis_ticker_raises_a_thesis_review_notice_and_still_the_filing_alert()
    {
        var thesis = Thesis("NVDA");
        _theses.Setup(t => t.ListUnscopedAsync(_userId, default)).ReturnsAsync([thesis]);
        FilingsFor("NVDA", Filing("NVDA"));

        await _job.ExecuteAsync(_nowUtc);

        var review = _reviews.Should().ContainSingle().Subject;
        review.Subject.Should().Be(ReviewSubjectKind.Thesis);
        review.SubjectId.Should().Be(thesis.Id);
        review.Ticker.Should().Be("NVDA");
        review.Source.Should().Be(ReviewDueSource.Filing);
        review.Reference.Should().Be("0001-26-000123");
        review.Evidence.Should().Contain("filing:0001-26-000123");
        review.Headline.Should().Contain("10-Q filed 2026-09-21");
        ReviewDueSource.IsRealtime(review.Source).Should().BeTrue();
        _alerts.Verify(a => a.GenerateFilingLandedAlertAsync(
            _userId, "NVDA", "10-Q", _today, "0001-26-000123", It.IsAny<string>(), default), Times.Once);
    }

    [Fact]
    public async Task A_filing_on_a_trigger_proxy_notifies_the_thesis_that_names_it()
    {
        var thesis = Thesis("SOXX", proxy: "MU");
        _theses.Setup(t => t.ListUnscopedAsync(_userId, default)).ReturnsAsync([thesis]);
        FilingsFor("SOXX");
        FilingsFor("MU", Filing("MU", "8-K", accession: "0002-26-000001"));

        await _job.ExecuteAsync(_nowUtc);

        var review = _reviews.Should().ContainSingle().Subject;
        review.SubjectId.Should().Be(thesis.Id);
        review.Ticker.Should().Be("SOXX", "the notice opens the thesis's own ticker, not the proxy's");
        review.Headline.Should().Contain("MU").And.Contain("proxy");
    }

    [Fact]
    public async Task A_filing_on_a_held_name_with_neither_a_thesis_nor_a_candidate_raises_no_review_notice()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        FilingsFor("AAPL", Filing("AAPL"));

        await _job.ExecuteAsync(_nowUtc);

        _reviews.Should().BeEmpty();
        _alerts.Verify(a => a.GenerateFilingLandedAlertAsync(
            _userId, "AAPL", "10-Q", _today, "0001-26-000123", It.IsAny<string>(), default), Times.Once);
    }

    [Fact]
    public async Task A_filing_on_an_Active_candidate_raises_a_candidate_notice_without_a_filing_alert()
    {
        var candidate = Candidate("CBRS");
        _candidates.Setup(c => c.ListAllUnscopedAsync(_userId, default)).ReturnsAsync([candidate]);
        FilingsFor("CBRS", Filing("CBRS", "10-K", accession: "0003-26-000009"));

        await _job.ExecuteAsync(_nowUtc);

        var review = _reviews.Should().ContainSingle().Subject;
        review.Subject.Should().Be(ReviewSubjectKind.Candidate);
        review.SubjectId.Should().Be(candidate.Id);
        review.Source.Should().Be(ReviewDueSource.Filing);
        review.Reference.Should().Be("0003-26-000009");
        _alerts.Verify(a => a.GenerateFilingLandedAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<string>(),
            It.IsAny<string>(), default), Times.Never, "a candidate is not a holding");
    }

    [Theory]
    [InlineData(CandidateStatus.Promoted)]
    [InlineData(CandidateStatus.Rejected)]
    [InlineData(CandidateStatus.Expired)]
    public async Task A_candidate_that_is_no_longer_Active_is_not_watched(CandidateStatus status)
    {
        _candidates.Setup(c => c.ListAllUnscopedAsync(_userId, default)).ReturnsAsync([Candidate("CBRS", status)]);
        FilingsFor("CBRS", Filing("CBRS"));

        await _job.ExecuteAsync(_nowUtc);

        _reviews.Should().BeEmpty();
        _secEdgar.Verify(e => e.GetRecentFilingsAsync(
            "CBRS", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), default), Times.Never);
    }

    [Fact]
    public async Task A_ticker_with_both_a_thesis_and_a_candidate_is_covered_by_the_thesis_alone()
    {
        var thesis = Thesis("NVDA");
        _theses.Setup(t => t.ListUnscopedAsync(_userId, default)).ReturnsAsync([thesis]);
        _candidates.Setup(c => c.ListAllUnscopedAsync(_userId, default)).ReturnsAsync([Candidate("NVDA")]);
        FilingsFor("NVDA", Filing("NVDA"));

        await _job.ExecuteAsync(_nowUtc);

        _reviews.Should().ContainSingle().Which.Subject.Should().Be(ReviewSubjectKind.Thesis);
    }

    [Fact]
    public async Task Re_reading_the_same_filing_hands_the_generator_the_same_notice_so_it_can_stay_once_only()
    {
        _theses.Setup(t => t.ListUnscopedAsync(_userId, default)).ReturnsAsync([Thesis("NVDA")]);
        FilingsFor("NVDA", Filing("NVDA"));

        await _job.ExecuteAsync(_nowUtc);
        await _job.ExecuteAsync(_nowUtc.AddHours(1));

        _reviews.Should().HaveCount(2);
        _reviews[1].Should().BeEquivalentTo(_reviews[0]);
    }

    [Fact]
    public async Task A_filing_outside_the_recent_window_raises_nothing()
    {
        _theses.Setup(t => t.ListUnscopedAsync(_userId, default)).ReturnsAsync([Thesis("NVDA")]);
        FilingsFor("NVDA", Filing("NVDA", date: _today.AddDays(-10)));

        await _job.ExecuteAsync(_nowUtc);

        _reviews.Should().BeEmpty();
    }

    [Fact]
    public async Task Two_theses_on_one_ticker_are_each_notified()
    {
        var first = Thesis("NVDA");
        var second = Thesis("NVDA");
        _theses.Setup(t => t.ListUnscopedAsync(_userId, default)).ReturnsAsync([first, second]);
        FilingsFor("NVDA", Filing("NVDA"));

        await _job.ExecuteAsync(_nowUtc);

        _reviews.Select(r => r.SubjectId).Should().BeEquivalentTo([first.Id, second.Id]);
    }
}
