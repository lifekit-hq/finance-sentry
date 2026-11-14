namespace FinanceSentry.Modules.Research.Tests.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>A news cluster on a coverage name is noted for the coverage review; only an alert-universe name also gets the NewsCluster alert.</summary>
public sealed class NewsMaterialityCoverageTests : CoverageDetectorFixture
{
    private static readonly DateTimeOffset NowUtc = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<INewsRepository> _news = new();
    private readonly Mock<IMaterialityTermRepository> _terms = new();

    public NewsMaterialityCoverageTests()
    {
        _terms.Setup(t => t.ListEnabledTermsAsync(default)).ReturnsAsync([]);
        _news.Setup(n => n.GetForTickerAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), default)).ReturnsAsync([]);
        _news.Setup(n => n.SearchAsync(null, null, It.IsAny<Guid?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), default)).ReturnsAsync([]);
    }

    private NewsMaterialityJob Job(params string[] coverage)
        => new(
            Banking.Object, Brokerage.Object, Universe(coverage), Theses.Object, _news.Object, _terms.Object, Alerts.Object,
            Raiser(), NullLogger<NewsMaterialityJob>.Instance);

    private void Cluster(string ticker)
        => _news.Setup(n => n.GetForTickerAsync(ticker, It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), default))
            .ReturnsAsync(
            [
                Article(ticker, "src:Reuters", $"{ticker} one"),
                Article(ticker, "src:Bloomberg", $"{ticker} two"),
            ]);

    private static NewsArticle Article(string ticker, string source, string title) => new()
    {
        Source = source,
        Title = title,
        Url = $"https://example.test/{Guid.NewGuid()}",
        Tickers = [ticker],
        PublishedAt = NowUtc,
        ContentHash = Guid.NewGuid().ToString("N"),
    };

    [Fact]
    public async Task A_cluster_on_a_coverage_only_name_is_noted_without_a_news_alert()
    {
        Cluster("MU");

        await Job("MU").ExecuteAsync(NowUtc);

        var hit = Hits.Should().ContainSingle().Subject;
        hit.Ticker.Should().Be("MU");
        hit.Source.Should().Be(ReviewDueSource.News);
        hit.Reference.Should().Be("MU:2026-11-16");
        Alerts.Verify(a => a.GenerateNewsClusterAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), default), Times.Never);
    }

    [Fact]
    public async Task A_held_coverage_name_keeps_its_news_alert_and_is_also_noted()
    {
        Hold("NVDA");
        Cluster("NVDA");

        await Job("NVDA").ExecuteAsync(NowUtc);

        Alerts.Verify(a => a.GenerateNewsClusterAlertAsync(
            UserId, "NVDA", It.IsAny<string>(), new DateOnly(2026, 11, 16), default), Times.Once);
        Hits.Should().ContainSingle().Which.Ticker.Should().Be("NVDA");
    }

    [Fact]
    public async Task A_quiet_coverage_name_notes_nothing_and_the_reviewer_is_not_called()
    {
        await Job("MU").ExecuteAsync(NowUtc);

        Hits.Should().BeEmpty();
        ReviewerCalls.Should().Be(0);
    }
}
