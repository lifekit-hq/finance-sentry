namespace FinanceSentry.Modules.Research.Tests.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using FinanceSentry.Modules.Research.Tests.Companion;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// #825 pick 6 (notifications A): one rating-change alert per held name per day, summarising every firm
/// that moved; watchlist names only when the profile toggle is on; the alert opens the dossier's Analyst card.
/// </summary>
public sealed class AnalystRatingChangeAlertJobTests
{
    private static readonly DateTime NowUtc = new(2026, 10, 7, 1, 30, 0, DateTimeKind.Utc);
    private static readonly DateOnly Yesterday = new(2026, 10, 6);

    private readonly Mock<IBankingTotalsReader> _banking = new();
    private readonly Mock<IBrokerageHoldingsReader> _brokerage = new();
    private readonly Mock<IThesisRepository> _theses = new();
    private readonly Mock<IWatchlistReader> _watchlist = new();
    private readonly Mock<ICryptoHoldingsReader> _crypto = new();
    private readonly Mock<IUserAlertPreferencesReader> _prefs = new();
    private readonly FakeAnalystActionRepository _actions = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly List<(string Ticker, DateOnly Day, IReadOnlyList<AnalystRatingChange> Changes)> _raised = [];
    private readonly AnalystRatingChangeAlertJob _job;

    public AnalystRatingChangeAlertJobTests()
    {
        var alerts = new Mock<IAlertGeneratorService>();
        alerts.Setup(a => a.GenerateAnalystRatingChangeAlertAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateOnly>(),
                It.IsAny<IReadOnlyList<AnalystRatingChange>>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, string t, DateOnly d, IReadOnlyList<AnalystRatingChange> c, CancellationToken _) =>
                _raised.Add((t, d, c)))
            .Returns(Task.CompletedTask);

        _job = new AnalystRatingChangeAlertJob(
            _banking.Object,
            new LookaheadUniverse(_brokerage.Object, _watchlist.Object, _theses.Object, _crypto.Object),
            _actions,
            _prefs.Object,
            alerts.Object,
            NullLogger<AnalystRatingChangeAlertJob>.Instance);

        _banking.Setup(b => b.GetActiveUserIdsAsync(default)).ReturnsAsync([_userId]);
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default)).ReturnsAsync(
            [new BrokerageHoldingSummary("NVDA", "STK", 5m, 900m, DateTime.UtcNow, "IBKR")]);
        _theses.Setup(t => t.ListUnscopedAsync(_userId, default)).ReturnsAsync([]);
        _watchlist.Setup(w => w.ListTickersAsync(_userId, default)).ReturnsAsync(["AMD"]);
        _prefs.Setup(p => p.GetAsync(_userId, default))
            .ReturnsAsync(new UserAlertPreferences(true, 0m, true, WatchlistAnalystAlerts: false));
    }

    private void Given(string ticker, string firm, AnalystActionType type, DateOnly? day = null)
        => _actions.Actions.Add(new AnalystAction
        {
            Ticker = ticker,
            Firm = firm,
            ActionType = type,
            PriorRating = "Neutral",
            NewRating = type == AnalystActionType.Upgrade ? "Buy" : "Sell",
            ActionDate = day ?? Yesterday,
        });

    [Fact]
    public async Task Execute_SeveralFirmsOnOneHeldNameInADay_RaisesOneSummarisingAlert()
    {
        Given("NVDA", "Goldman Sachs", AnalystActionType.Upgrade);
        Given("NVDA", "Barclays", AnalystActionType.Upgrade);
        Given("NVDA", "Citi", AnalystActionType.Downgrade);

        await _job.ExecuteAsync(NowUtc);

        var raised = _raised.Should().ContainSingle().Subject;
        raised.Ticker.Should().Be("NVDA");
        raised.Day.Should().Be(Yesterday);
        raised.Changes.Select(c => c.Firm).Should().BeEquivalentTo("Goldman Sachs", "Barclays", "Citi");
        raised.Changes.Count(c => c.IsUpgrade).Should().Be(2);
    }

    [Fact]
    public async Task Execute_ActionsOnTwoDays_RaiseOneAlertPerDay()
    {
        Given("NVDA", "Goldman Sachs", AnalystActionType.Upgrade);
        Given("NVDA", "Barclays", AnalystActionType.Downgrade, Yesterday.AddDays(1));

        await _job.ExecuteAsync(NowUtc);

        _raised.Select(r => r.Day).Should().BeEquivalentTo([Yesterday, Yesterday.AddDays(1)]);
    }

    [Fact]
    public async Task Execute_WatchlistNameWithoutOptIn_RaisesNothing()
    {
        Given("AMD", "Goldman Sachs", AnalystActionType.Upgrade);

        await _job.ExecuteAsync(NowUtc);

        _raised.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_WatchlistNameWithOptIn_RaisesAlert()
    {
        _prefs.Setup(p => p.GetAsync(_userId, default))
            .ReturnsAsync(new UserAlertPreferences(true, 0m, true, WatchlistAnalystAlerts: true));
        Given("AMD", "Goldman Sachs", AnalystActionType.Upgrade);
        Given("NVDA", "Barclays", AnalystActionType.Downgrade);

        await _job.ExecuteAsync(NowUtc);

        _raised.Select(r => r.Ticker).Should().BeEquivalentTo("AMD", "NVDA");
    }

    [Fact]
    public async Task Execute_HeldNameWithoutOptIn_StillAlerts()
    {
        Given("NVDA", "Goldman Sachs", AnalystActionType.Upgrade);

        await _job.ExecuteAsync(NowUtc);

        _raised.Should().ContainSingle(r => r.Ticker == "NVDA");
    }

    [Fact]
    public async Task Execute_NonRatingActionsAndStaleActions_RaiseNothing()
    {
        Given("NVDA", "Goldman Sachs", AnalystActionType.Initiate);
        Given("NVDA", "Barclays", AnalystActionType.Upgrade, Yesterday.AddDays(-10));
        Given("TSLA", "Citi", AnalystActionType.Downgrade);

        await _job.ExecuteAsync(NowUtc);

        _raised.Should().BeEmpty();
    }

    [Fact]
    public void The_alert_opens_the_dossier_analyst_card()
        => AlertAppPath.Resolve("AnalystRatingChange", null, "NVDA", DateTimeOffset.UtcNow)
            .Should().Be("/assets/NVDA#analyst-coverage");
}
