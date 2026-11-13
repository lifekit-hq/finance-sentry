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
/// A news cluster on a ticker that maps to a thesis (own ticker or trigger proxy) or an Active candidate raises the digest-lane
/// review notice, naming the cluster's articles. A candidate is scanned for the notice alone; a held name with neither stays
/// a plain NewsCluster.
/// </summary>
public sealed class NewsMaterialityReviewDueTests
{
    private readonly Mock<IBankingTotalsReader> _banking = new();
    private readonly Mock<IBrokerageHoldingsReader> _brokerage = new();
    private readonly Mock<IThesisRepository> _theses = new();
    private readonly Mock<ICandidateRepository> _candidates = new();
    private readonly Mock<INewsRepository> _news = new();
    private readonly Mock<IMaterialityTermRepository> _terms = new();
    private readonly Mock<IAlertGeneratorService> _alerts = new();
    private readonly List<ReviewDue> _reviews = [];
    private readonly NewsMaterialityJob _job;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly DateTimeOffset _nowUtc = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly DateOnly _today = new(2026, 9, 21);

    public NewsMaterialityReviewDueTests()
    {
        _job = new NewsMaterialityJob(
            _banking.Object, _brokerage.Object, _theses.Object, _news.Object, _terms.Object, _alerts.Object,
            new ReviewDueRaiser(_theses.Object, _candidates.Object, _alerts.Object),
            NullLogger<NewsMaterialityJob>.Instance);

        _banking.Setup(b => b.GetActiveUserIdsAsync(default)).ReturnsAsync([_userId]);
        _terms.Setup(m => m.ListEnabledTermsAsync(default)).ReturnsAsync(["guidance"]);
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default)).ReturnsAsync([]);
        _theses.Setup(t => t.ListUnscopedAsync(_userId, default)).ReturnsAsync([]);
        _candidates.Setup(c => c.ListAllUnscopedAsync(_userId, default)).ReturnsAsync([]);
        _news.Setup(n => n.GetForTickerAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), default))
            .ReturnsAsync([]);
        _news.Setup(n => n.SearchAsync(null, null, It.IsAny<Guid?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), default))
            .ReturnsAsync([]);
        _alerts.Setup(a => a.GenerateReviewDueAlertAsync(_userId, It.IsAny<ReviewDue>(), default))
            .Callback<Guid, ReviewDue, CancellationToken>((_, review, _) => _reviews.Add(review))
            .Returns(Task.CompletedTask);
    }

    private static NewsArticle Article(string ticker, string source, string title) => new()
    {
        Source = source,
        Title = title,
        Url = $"https://example.test/{Guid.NewGuid()}",
        Tickers = [ticker],
        PublishedAt = DateTimeOffset.UtcNow,
        ContentHash = Guid.NewGuid().ToString("N"),
    };

    private void Cluster(string ticker, out NewsArticle first, out NewsArticle second)
    {
        first = Article(ticker, "src:Reuters", $"{ticker} headline one");
        second = Article(ticker, "src:Bloomberg", $"{ticker} headline two");
        _news.Setup(n => n.GetForTickerAsync(ticker, It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), default))
            .ReturnsAsync([first, second]);
    }

    [Fact]
    public async Task A_cluster_on_a_thesis_ticker_raises_a_digest_lane_thesis_notice_naming_the_articles()
    {
        var thesis = new InvestmentThesis { Id = Guid.NewGuid(), UserId = _userId, Ticker = "AAPL" };
        _theses.Setup(t => t.ListUnscopedAsync(_userId, default)).ReturnsAsync([thesis]);
        Cluster("AAPL", out var first, out var second);

        await _job.ExecuteAsync(_nowUtc);

        var review = _reviews.Should().ContainSingle().Subject;
        review.Subject.Should().Be(ReviewSubjectKind.Thesis);
        review.SubjectId.Should().Be(thesis.Id);
        review.Source.Should().Be(ReviewDueSource.News);
        review.Reference.Should().Be("AAPL:2026-09-21");
        review.Evidence.Should().BeEquivalentTo([$"article:{first.Id}", $"article:{second.Id}"]);
        ReviewDueSource.IsRealtime(review.Source).Should().BeFalse();
        _alerts.Verify(a => a.GenerateNewsClusterAlertAsync(
            _userId, "AAPL", It.IsAny<string>(), _today, default), Times.Once);
    }

    [Fact]
    public async Task A_cluster_on_a_trigger_proxy_notifies_the_thesis_that_names_it()
    {
        var thesis = new InvestmentThesis
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            Ticker = "SOXX",
            InvalidationTriggers = [new ThesisInvalidationTrigger("gross_margin", "lessThan", 0.35m, "MU")],
        };
        _theses.Setup(t => t.ListUnscopedAsync(_userId, default)).ReturnsAsync([thesis]);
        Cluster("MU", out _, out _);

        await _job.ExecuteAsync(_nowUtc);

        var review = _reviews.Should().ContainSingle().Subject;
        review.SubjectId.Should().Be(thesis.Id);
        review.Ticker.Should().Be("SOXX");
        review.Reference.Should().Be("MU:2026-09-21", "the hit ticker is part of the identity, so SOXX's own cluster stays distinct");
    }

    [Fact]
    public async Task A_cluster_on_an_Active_candidate_raises_a_candidate_notice_without_a_NewsCluster_alert()
    {
        var candidate = new OpportunityCandidate { Id = Guid.NewGuid(), Ticker = "CBRS", Status = CandidateStatus.Active };
        _candidates.Setup(c => c.ListAllUnscopedAsync(_userId, default)).ReturnsAsync([candidate]);
        Cluster("CBRS", out _, out _);

        await _job.ExecuteAsync(_nowUtc);

        var review = _reviews.Should().ContainSingle().Subject;
        review.Subject.Should().Be(ReviewSubjectKind.Candidate);
        review.SubjectId.Should().Be(candidate.Id);
        review.Source.Should().Be(ReviewDueSource.News);
        _alerts.Verify(a => a.GenerateNewsClusterAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), default), Times.Never);
    }

    [Fact]
    public async Task A_cluster_on_a_held_name_with_neither_a_thesis_nor_a_candidate_stays_a_plain_NewsCluster()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        Cluster("AAPL", out _, out _);

        await _job.ExecuteAsync(_nowUtc);

        _reviews.Should().BeEmpty();
        _alerts.Verify(a => a.GenerateNewsClusterAlertAsync(
            _userId, "AAPL", It.IsAny<string>(), _today, default), Times.Once);
    }

    [Fact]
    public async Task A_quiet_thesis_ticker_raises_nothing()
    {
        _theses.Setup(t => t.ListUnscopedAsync(_userId, default))
            .ReturnsAsync([new InvestmentThesis { Id = Guid.NewGuid(), UserId = _userId, Ticker = "AAPL" }]);
        _news.Setup(n => n.GetForTickerAsync("AAPL", It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), default))
            .ReturnsAsync([Article("AAPL", "src:Reuters", "AAPL launches new product")]);

        await _job.ExecuteAsync(_nowUtc);

        _reviews.Should().BeEmpty();
    }

    [Fact]
    public async Task The_next_half_hourly_run_hands_the_generator_the_same_notice_so_it_stays_once_only_per_day()
    {
        _theses.Setup(t => t.ListUnscopedAsync(_userId, default))
            .ReturnsAsync([new InvestmentThesis { Id = Guid.NewGuid(), UserId = _userId, Ticker = "AAPL" }]);
        Cluster("AAPL", out _, out _);

        await _job.ExecuteAsync(_nowUtc);
        await _job.ExecuteAsync(_nowUtc.AddMinutes(30));

        _reviews.Should().HaveCount(2);
        _reviews[1].Reference.Should().Be(_reviews[0].Reference);
        _reviews[1].SubjectId.Should().Be(_reviews[0].SubjectId);
    }
}
