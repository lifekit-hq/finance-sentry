namespace FinanceSentry.Modules.Research.Tests.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// N1 (ledger-heartbeat design): a held name or thesis keyword clusters in the news — two or more
/// sources within 2h, any thesis-attached source hit, or a material-class keyword. The loudest signal
/// in the design by observed volume, so its noise controls (dedup by ticker+day, one alert per
/// qualifying cluster, no re-fire on a re-run) are proven here rather than assumed.
/// </summary>
public sealed class NewsMaterialityJobTests
{
    private readonly Mock<IBankingTotalsReader> _banking = new();
    private readonly Mock<IBrokerageHoldingsReader> _brokerage = new();
    private readonly Mock<IThesisRepository> _theses = new();
    private readonly Mock<INewsRepository> _news = new();
    private readonly Mock<IAlertGeneratorService> _alerts = new();
    private readonly NewsMaterialityJob _job;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly DateTimeOffset _nowUtc = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly DateOnly _today = DateOnly.FromDateTime(new DateTime(2026, 9, 21));

    public NewsMaterialityJobTests()
    {
        _job = new NewsMaterialityJob(
            _banking.Object,
            _brokerage.Object,
            _theses.Object,
            _news.Object,
            _alerts.Object,
            NullLogger<NewsMaterialityJob>.Instance);

        _banking.Setup(b => b.GetActiveUserIdsAsync(default)).ReturnsAsync([_userId]);
        _theses.Setup(t => t.ListAsync(_userId, default)).ReturnsAsync([]);
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        _news.Setup(n => n.GetForTickerAsync("AAPL", It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), default))
            .ReturnsAsync([]);
        _news.Setup(n => n.SearchAsync(
                null, null, It.IsAny<Guid?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), default))
            .ReturnsAsync([]);
    }

    private static NewsArticle Article(string source, string title, string? summary = null)
        => new()
        {
            Source = source,
            Title = title,
            Url = $"https://example.test/{Guid.NewGuid()}",
            Summary = summary,
            Tickers = ["AAPL"],
            ThesisIds = [],
            PublishedAt = DateTimeOffset.UtcNow,
            ContentHash = Guid.NewGuid().ToString("N"),
        };

    [Fact]
    public async Task Execute_NoActiveUsers_SkipsGracefully()
    {
        _banking.Setup(b => b.GetActiveUserIdsAsync(default)).ReturnsAsync([]);

        await _job.ExecuteAsync(_nowUtc);

        _brokerage.Verify(b => b.GetHoldingsAsync(It.IsAny<Guid>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_NoHoldingsOrTheses_SkipsFetchEntirely()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default)).ReturnsAsync([]);

        await _job.ExecuteAsync(_nowUtc);

        _news.Verify(n => n.GetForTickerAsync(
            It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_TwoDistinctSourcesWithinWindow_FiresOnce()
    {
        _news.Setup(n => n.GetForTickerAsync("AAPL", It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), default))
            .ReturnsAsync([Article("src:Reuters", "AAPL headline one"), Article("src:Bloomberg", "AAPL headline two")]);

        await _job.ExecuteAsync(_nowUtc);

        _alerts.Verify(a => a.GenerateNewsClusterAlertAsync(
            _userId, "AAPL", It.IsAny<string>(), _today, default), Times.Once);
    }

    [Fact]
    public async Task Execute_SingleSource_NoThesisHit_NoKeyword_DoesNotFire()
    {
        _news.Setup(n => n.GetForTickerAsync("AAPL", It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), default))
            .ReturnsAsync([Article("src:Reuters", "AAPL launches new product")]);

        await _job.ExecuteAsync(_nowUtc);

        _alerts.Verify(a => a.GenerateNewsClusterAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), default), Times.Never);
    }

    /// <summary>
    /// Registered-source articles (thesis keyword sources, N2 geopolitics feeds) come out of
    /// ingestion tagged with the thesis but never with a ticker — the detector must still see them.
    /// </summary>
    private static NewsArticle RegisteredSourceArticle(string source, string title, Guid thesisId)
        => new()
        {
            Source = source,
            Title = title,
            Url = $"https://example.test/{Guid.NewGuid()}",
            Tickers = [],
            ThesisIds = [thesisId],
            PublishedAt = DateTimeOffset.UtcNow,
            ContentHash = Guid.NewGuid().ToString("N"),
        };

    private Guid SetupThesis(string ticker)
    {
        var thesisId = Guid.NewGuid();
        _theses.Setup(t => t.ListAsync(_userId, default)).ReturnsAsync([
            new InvestmentThesis { Id = thesisId, UserId = _userId, Ticker = ticker },
        ]);
        return thesisId;
    }

    private void SetupThesisArticles(Guid thesisId, params NewsArticle[] articles)
        => _news.Setup(n => n.SearchAsync(
                null, null, thesisId, It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), default))
            .ReturnsAsync(articles);

    [Fact]
    public async Task Execute_ThesisKeywordSourceHit_WithNoTickerTag_Fires()
    {
        var thesisId = SetupThesis("AAPL");
        SetupThesisArticles(thesisId, RegisteredSourceArticle("src:TrendForce Press Center", "Supplier update", thesisId));

        await _job.ExecuteAsync(_nowUtc);

        _alerts.Verify(a => a.GenerateNewsClusterAlertAsync(
            _userId, "AAPL", It.Is<string>(r => r.Contains("thesis")), _today, default), Times.Once);
    }

    [Fact]
    public async Task Execute_GeopoliticsSourceArticle_WithNoTickerTag_Fires()
    {
        var thesisId = SetupThesis("AAPL");
        SetupThesisArticles(
            thesisId,
            RegisteredSourceArticle("src:Google News: AAPL geopolitics", "New tariffs hit handset imports", thesisId));

        await _job.ExecuteAsync(_nowUtc);

        _alerts.Verify(a => a.GenerateNewsClusterAlertAsync(
            _userId, "AAPL", It.IsAny<string>(), _today, default), Times.Once);
    }

    [Fact]
    public async Task Execute_ThesisSourceArticle_WithProxyTrigger_FiresOnlyForTheThesisTicker()
    {
        var thesisId = Guid.NewGuid();
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default)).ReturnsAsync([]);
        _theses.Setup(t => t.ListAsync(_userId, default)).ReturnsAsync([
            new InvestmentThesis
            {
                Id = thesisId,
                UserId = _userId,
                Ticker = "NVDA",
                InvalidationTriggers = [new ThesisInvalidationTrigger("gross_margin", "lessThan", 0.35m, "SOXX")],
            },
        ]);
        _news.Setup(n => n.GetForTickerAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), default))
            .ReturnsAsync([]);
        SetupThesisArticles(
            thesisId,
            RegisteredSourceArticle("src:Google News: NVDA geopolitics", "Export curbs widen", thesisId));

        await _job.ExecuteAsync(_nowUtc);

        _alerts.Verify(a => a.GenerateNewsClusterAlertAsync(
            _userId, "NVDA", It.IsAny<string>(), _today, default), Times.Once);
        _alerts.Verify(a => a.GenerateNewsClusterAlertAsync(
            It.IsAny<Guid>(), "SOXX", It.IsAny<string>(), It.IsAny<DateOnly>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_TickerFeedPlusThesisSource_CountsAsTwoSources()
    {
        var thesisId = SetupThesis("AAPL");
        _news.Setup(n => n.GetForTickerAsync("AAPL", It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), default))
            .ReturnsAsync([Article("yahoo:AAPL", "AAPL launches new product")]);
        SetupThesisArticles(
            thesisId,
            RegisteredSourceArticle("src:Google News: AAPL geopolitics", "Export curbs widen", thesisId));

        await _job.ExecuteAsync(_nowUtc);

        _alerts.Verify(a => a.GenerateNewsClusterAlertAsync(
            _userId, "AAPL", It.Is<string>(r => r.Contains("2 sources")), _today, default), Times.Once);
    }

    [Theory]
    [InlineData("guidance")]
    [InlineData("downgrade")]
    [InlineData("investigation")]
    [InlineData("M&A")]
    [InlineData("halted")]
    [InlineData("recall")]
    [InlineData("acquisition")]
    public async Task Execute_SingleSource_MaterialKeywordInTitle_Fires(string keyword)
    {
        _news.Setup(n => n.GetForTickerAsync("AAPL", It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), default))
            .ReturnsAsync([Article("src:Reuters", $"AAPL {keyword} announced")]);

        await _job.ExecuteAsync(_nowUtc);

        _alerts.Verify(a => a.GenerateNewsClusterAlertAsync(
            _userId, "AAPL", It.Is<string>(r => r.Contains(keyword)), _today, default), Times.Once);
    }

    [Fact]
    public async Task Execute_MaterialKeywordInSummaryOnly_Fires()
    {
        _news.Setup(n => n.GetForTickerAsync("AAPL", It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), default))
            .ReturnsAsync([Article("src:Reuters", "AAPL update", summary: "Analysts cut guidance for the quarter.")]);

        await _job.ExecuteAsync(_nowUtc);

        _alerts.Verify(a => a.GenerateNewsClusterAlertAsync(
            _userId, "AAPL", It.IsAny<string>(), _today, default), Times.Once);
    }

    [Fact]
    public async Task Execute_ThesisProxyTicker_IsIncludedInTheFetch()
    {
        var thesisId = Guid.NewGuid();
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default)).ReturnsAsync([]);
        _theses.Setup(t => t.ListAsync(_userId, default)).ReturnsAsync([
            new InvestmentThesis
            {
                Id = thesisId,
                UserId = _userId,
                Ticker = "SOXX",
                InvalidationTriggers = [new ThesisInvalidationTrigger("gross_margin", "lessThan", 0.35m, "MU")],
            },
        ]);
        _news.Setup(n => n.GetForTickerAsync("SOXX", It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), default))
            .ReturnsAsync([]);
        _news.Setup(n => n.GetForTickerAsync("MU", It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), default))
            .ReturnsAsync([Article("src:Reuters", "MU acquisition talk")]);

        await _job.ExecuteAsync(_nowUtc);

        _alerts.Verify(a => a.GenerateNewsClusterAlertAsync(
            _userId, "MU", It.IsAny<string>(), _today, default), Times.Once);
    }

    /// <summary>
    /// The job re-asks with the identical (ticker, day) on every 30-min run — the actual "does not
    /// fire again" guarantee lives in AlertGeneratorService's (ticker, day) dedup (covered there), but
    /// that only holds if the job keeps deriving the same day and ticker as the calendar day it re-runs
    /// inside.
    /// </summary>
    [Fact]
    public async Task Execute_SameClusterNextRunSameDay_AsksForTheIdenticalReferenceAgain()
    {
        _news.Setup(n => n.GetForTickerAsync("AAPL", It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), default))
            .ReturnsAsync([Article("src:Reuters", "AAPL headline one"), Article("src:Bloomberg", "AAPL headline two")]);

        await _job.ExecuteAsync(_nowUtc);
        await _job.ExecuteAsync(_nowUtc.AddMinutes(30));

        _alerts.Verify(a => a.GenerateNewsClusterAlertAsync(
            _userId, "AAPL", It.IsAny<string>(), _today, default), Times.Exactly(2));
    }

    [Fact]
    public async Task Execute_EmptyFeed_EmitsNothing()
    {
        _news.Setup(n => n.GetForTickerAsync("AAPL", It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), default))
            .ReturnsAsync([]);

        await _job.ExecuteAsync(_nowUtc);

        _alerts.Verify(a => a.GenerateNewsClusterAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_UserThrows_ContinuesToNextUser()
    {
        var userId2 = Guid.NewGuid();
        _banking.Setup(b => b.GetActiveUserIdsAsync(default)).ReturnsAsync([_userId, userId2]);
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ThrowsAsync(new InvalidOperationException("simulated error"));
        _brokerage.Setup(b => b.GetHoldingsAsync(userId2, default))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        _theses.Setup(t => t.ListAsync(userId2, default)).ReturnsAsync([]);
        _news.Setup(n => n.GetForTickerAsync("AAPL", It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), default))
            .ReturnsAsync([Article("src:Reuters", "AAPL headline one"), Article("src:Bloomberg", "AAPL headline two")]);

        await _job.ExecuteAsync(_nowUtc);

        _alerts.Verify(a => a.GenerateNewsClusterAlertAsync(
            userId2, "AAPL", It.IsAny<string>(), _today, default), Times.Once);
    }

    [Fact]
    public async Task Execute_NonEquityHolding_IsNotIncludedInTheFetch()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("BTC", "CRYPTO", 1m, 50000m, DateTime.UtcNow, "Binance")]);

        await _job.ExecuteAsync(_nowUtc);

        _news.Verify(n => n.GetForTickerAsync(
            It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), default), Times.Never);
    }
}
