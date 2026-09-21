namespace FinanceSentry.Modules.Research.Tests.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// T2 (ledger-heartbeat design): a 10-K/10-Q/8-K on a holding or thesis-proxy ticker. EDGAR has no
/// contract, so a missing field, an empty submissions list or a failed fetch must produce silence,
/// never an alert storm or a thrown exception. Dedup by accession number is the whole design — an
/// hourly job re-reads the same submissions repeatedly.
/// </summary>
public sealed class FilingWatchJobTests
{
    private readonly Mock<IBankingTotalsReader> _banking = new();
    private readonly Mock<IBrokerageHoldingsReader> _brokerage = new();
    private readonly Mock<IThesisRepository> _theses = new();
    private readonly Mock<ISecEdgarService> _secEdgar = new();
    private readonly Mock<IAlertGeneratorService> _alerts = new();
    private readonly FilingWatchJob _job;

    private readonly Guid _userId = Guid.NewGuid();

    // Fixed to a Monday so trading-day gating never makes these tests flaky depending on the day they run.
    private readonly DateTime _nowUtc = new(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);
    private readonly DateOnly _today = DateOnly.FromDateTime(new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc));

    public FilingWatchJobTests()
    {
        _job = new FilingWatchJob(
            _banking.Object,
            _brokerage.Object,
            _theses.Object,
            _secEdgar.Object,
            _alerts.Object,
            NullLogger<FilingWatchJob>.Instance);

        _banking.Setup(b => b.GetActiveUserIdsAsync(default)).ReturnsAsync([_userId]);
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default)).ReturnsAsync([]);
        _theses.Setup(t => t.ListAsync(_userId, default)).ReturnsAsync([]);
    }

    private EdgarFiling Filing(string ticker, string form, DateOnly? filingDate = null, string accession = "0001-25-000123")
        => new(ticker, form, filingDate ?? _today, null, $"{form} description", accession,
            $"https://www.sec.gov/Archives/edgar/data/1/{accession}/doc.htm", true);

    [Fact]
    public async Task Execute_NoActiveUsers_SkipsGracefully()
    {
        _banking.Setup(b => b.GetActiveUserIdsAsync(default)).ReturnsAsync([]);

        await _job.ExecuteAsync(_nowUtc);

        _brokerage.Verify(b => b.GetHoldingsAsync(It.IsAny<Guid>(), default), Times.Never);
        _secEdgar.Verify(e => e.GetRecentFilingsAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_NoHoldingsOrTheses_SkipsFetchEntirely()
    {
        await _job.ExecuteAsync(_nowUtc);

        _secEdgar.Verify(e => e.GetRecentFilingsAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_NewQualifyingFilingOnHolding_EmitsOnce()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        _secEdgar.Setup(e => e.GetRecentFilingsAsync(
                "AAPL", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), default))
            .ReturnsAsync([Filing("AAPL", "10-Q")]);

        await _job.ExecuteAsync(_nowUtc);

        _alerts.Verify(a => a.GenerateFilingLandedAlertAsync(
            _userId, "AAPL", "10-Q", _today, "0001-25-000123", It.IsAny<string>(), default), Times.Once);
    }

    [Fact]
    public async Task Execute_ThesisProxyTicker_IsIncludedInTheFetch()
    {
        _theses.Setup(t => t.ListAsync(_userId, default)).ReturnsAsync([
            new InvestmentThesis
            {
                UserId = _userId,
                Ticker = "SOXX",
                InvalidationTriggers = [new ThesisInvalidationTrigger("gross_margin", "lessThan", 0.35m, "MU")],
            },
        ]);
        _secEdgar.Setup(e => e.GetRecentFilingsAsync(
                "MU", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), default))
            .ReturnsAsync([Filing("MU", "8-K")]);
        _secEdgar.Setup(e => e.GetRecentFilingsAsync(
                "SOXX", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), default))
            .ReturnsAsync([]);

        await _job.ExecuteAsync(_nowUtc);

        _alerts.Verify(a => a.GenerateFilingLandedAlertAsync(
            _userId, "MU", "8-K", _today, "0001-25-000123", It.IsAny<string>(), default), Times.Once);
    }

    /// <summary>
    /// The same accession number is fed to the alert generator on every hourly run — the actual
    /// "never twice" dedup lives in AlertGeneratorService (covered there), but that only holds if
    /// the job keeps asking with the identical (ticker, accession) pair as EDGAR keeps echoing it.
    /// </summary>
    [Fact]
    public async Task Execute_SameFiling_NextHourlyRun_AsksForTheIdenticalFilingAgain()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        _secEdgar.Setup(e => e.GetRecentFilingsAsync(
                "AAPL", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), default))
            .ReturnsAsync([Filing("AAPL", "10-Q")]);

        await _job.ExecuteAsync(_nowUtc);
        await _job.ExecuteAsync(_nowUtc);

        _alerts.Verify(a => a.GenerateFilingLandedAlertAsync(
            _userId, "AAPL", "10-Q", _today, "0001-25-000123", It.IsAny<string>(), default), Times.Exactly(2));
    }

    [Fact]
    public async Task Execute_RequestsOnlyCoveredFormTypes()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        // Form filtering is delegated to ISecEdgarService — the job only has to ask for 10-K/10-Q/8-K.
        _secEdgar.Setup(e => e.GetRecentFilingsAsync(
                "AAPL", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), default))
            .ReturnsAsync([]);

        await _job.ExecuteAsync(_nowUtc);

        _secEdgar.Verify(e => e.GetRecentFilingsAsync(
            "AAPL",
            It.Is<IReadOnlyCollection<string>>(f => f.SequenceEqual(new[] { "10-K", "10-Q", "8-K" })),
            It.IsAny<int>(), default), Times.Once);
    }

    [Fact]
    public async Task Execute_FilingDatedBeforeToday_IsNotAlertedOn()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        _secEdgar.Setup(e => e.GetRecentFilingsAsync(
                "AAPL", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), default))
            .ReturnsAsync([Filing("AAPL", "10-Q", _today.AddDays(-3))]);

        await _job.ExecuteAsync(_nowUtc);

        _alerts.Verify(a => a.GenerateFilingLandedAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(),
            It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_EmptySubmissionsList_EmitsNothing()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        _secEdgar.Setup(e => e.GetRecentFilingsAsync(
                "AAPL", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), default))
            .ReturnsAsync([]);

        await _job.ExecuteAsync(_nowUtc);

        _alerts.Verify(a => a.GenerateFilingLandedAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(),
            It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_SubmissionsFetchThrows_EmitsNothingAndDoesNotThrow()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        _secEdgar.Setup(e => e.GetRecentFilingsAsync(
                "AAPL", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), default))
            .ThrowsAsync(new HttpRequestException("EDGAR submissions unreachable"));

        await _job.ExecuteAsync(_nowUtc);

        _alerts.Verify(a => a.GenerateFilingLandedAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(),
            It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_MalformedFilingMissingAccessionNumber_IsSkipped()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);
        _secEdgar.Setup(e => e.GetRecentFilingsAsync(
                "AAPL", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), default))
            .ReturnsAsync([Filing("AAPL", "10-Q", accession: string.Empty)]);

        await _job.ExecuteAsync(_nowUtc);

        _alerts.Verify(a => a.GenerateFilingLandedAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(),
            It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_NonEquityHolding_IsNotIncludedInTheFetch()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("BTC", "CRYPTO", 1m, 50000m, DateTime.UtcNow, "Binance")]);

        await _job.ExecuteAsync(_nowUtc);

        _secEdgar.Verify(e => e.GetRecentFilingsAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), default), Times.Never);
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
        _secEdgar.Setup(e => e.GetRecentFilingsAsync(
                "AAPL", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), default))
            .ReturnsAsync([Filing("AAPL", "10-Q")]);

        // Should not throw — errors per user are caught
        await _job.ExecuteAsync(_nowUtc);

        _alerts.Verify(a => a.GenerateFilingLandedAlertAsync(
            userId2, "AAPL", "10-Q", _today, "0001-25-000123", It.IsAny<string>(), default), Times.Once);
    }

    [Theory]
    [InlineData(2026, 9, 19)] // Saturday
    [InlineData(2026, 9, 20)] // Sunday
    public async Task Execute_WeekendUtc_SkipsWithoutFetching(int year, int month, int day)
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);

        await _job.ExecuteAsync(new DateTime(year, month, day, 12, 0, 0, DateTimeKind.Utc));

        _banking.Verify(b => b.GetActiveUserIdsAsync(default), Times.Never);
        _secEdgar.Verify(e => e.GetRecentFilingsAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), default), Times.Never);
    }
}
