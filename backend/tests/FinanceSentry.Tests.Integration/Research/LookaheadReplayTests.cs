namespace FinanceSentry.Tests.Integration.Research;

using System.Net;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Integration;
using FinanceSentry.Modules.Alerts.Application.Services;
using FinanceSentry.Modules.Alerts.Domain;
using FinanceSentry.Modules.Alerts.Infrastructure.Persistence;
using FinanceSentry.Modules.Alerts.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.Companion.Application.Commands;
using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.Events.API.Responses;
using FinanceSentry.Modules.Events.Application.Commands;
using FinanceSentry.Modules.Events.Application.Queries;
using FinanceSentry.Modules.Events.Domain;
using FinanceSentry.Modules.Events.Infrastructure.Persistence;
using FinanceSentry.Modules.Events.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>
/// #698 replay harness: a known real provider event runs through the whole look-ahead path over real
/// Postgres — detector job → alert → companion event (disposition) → delivery → recorded verdict
/// (judgement) → the day's recorded outcome. Only the provider HTTP and the user's holdings/theses are
/// stubbed; the provider payloads are real responses captured in <c>Fixtures/</c>.
/// <list type="bullet">
/// <item>Earnings: Accenture (ACN, a thesis name, not a holding) reported on 2026-10-01. It entered the
/// three-day window on the 2026-09-28 06:00 UTC run, but the detector never looked it up because thesis
/// tickers were outside its universe.</item>
/// <item>Filing: Grab (GRAB, a foreign private issuer holding) filed three 6-Ks on 2026-09-15. The
/// detector only covered 10-K/10-Q/8-K, so a foreign issuer could never alert.</item>
/// </list>
/// </summary>
[Trait("Category", "Integration")]
public sealed class LookaheadReplayTests : IAsyncLifetime
{
    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "Research", "Fixtures");

    private static readonly DateTime AcnWindowRunUtc = new(2026, 9, 28, 6, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime GrabFilingDayRunUtc = new(2026, 9, 15, 14, 0, 0, DateTimeKind.Utc);

    private const string GrabFxAtomeAccession = "0001855612-26-000138";

    private TestDatabase? _database;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IBankingTotalsReader> _banking = new();
    private readonly Mock<IBrokerageHoldingsReader> _brokerage = new();
    private readonly Mock<IWatchlistReader> _watchlist = new();
    private readonly Mock<IThesisRepository> _theses = new();
    private readonly Mock<ICryptoHoldingsReader> _crypto = new();
    private readonly Mock<IPolicyAckReader> _policyAcks = new();
    private readonly Mock<IAnalystActionFeedReader> _analystActions = new();
    private readonly MaterialityPolicy _policy = new();

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres16.CreateDatabaseAsync();

        await using var alerts = AlertsContext();
        await alerts.Database.EnsureCreatedAsync();

        // Further contexts on the same database: EnsureCreated would see existing tables and skip them.
        await using var companion = CompanionContext();
        await companion.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        await using var events = EventsContext();
        await events.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();

        _banking.Setup(b => b.GetActiveUserIdsAsync(default)).ReturnsAsync([_userId]);
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default)).ReturnsAsync([]);
        _watchlist.Setup(w => w.ListTickersAsync(_userId, default)).ReturnsAsync([]);
        _theses.Setup(t => t.ListUnscopedAsync(_userId, default)).ReturnsAsync([]);
        _crypto.Setup(c => c.GetHoldingsAsync(_userId, default)).ReturnsAsync([]);
        _analystActions.Setup(a => a.GetNewSinceAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), default))
            .ReturnsAsync([]);
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    [DockerRequiredFact]
    public async Task AcnEarnings_ThesisName_RunsFromDetectorToRecordedVerdict()
    {
        _theses.Setup(t => t.ListUnscopedAsync(_userId, default))
            .ReturnsAsync([new InvestmentThesis { UserId = _userId, Ticker = "ACN" }]);

        await RunEarningsAheadAsync(AcnWindowRunUtc);

        var alert = (await AlertsOfTypeAsync(AlertType.EarningsAhead)).Should().ContainSingle().Subject;
        alert.ReferenceLabel.Should().Be("ACN");
        alert.Message.Should().Be("ACN reports earnings on 2026-10-01.");

        var outcome = await CaptureDeliverAndJudgeAsync(alert, "ACN Q4 FY26 print: in line, no thesis impact.");

        outcome.Kind.Should().Be(nameof(CompanionEventKind.EarningsAhead));
        outcome.Subject.Should().Be("ACN");
        outcome.Disposition.Should().Be(nameof(EventDisposition.Delivered));
        outcome.Outcome.Should().Be(EventOutcome.Verdict);

        // The next run inside the same window is deduplicated rather than alerting again.
        await RunEarningsAheadAsync(AcnWindowRunUtc.AddDays(1));
        (await AlertsOfTypeAsync(AlertType.EarningsAhead)).Should().ContainSingle();
    }

    [DockerRequiredFact]
    public async Task Grab6K_ForeignIssuerHolding_RunsFromDetectorToRecordedVerdict()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("GRAB", "STK", 100m, 500m, DateTime.UtcNow, "IBKR")]);

        await RunFilingWatchAsync(GrabFilingDayRunUtc);

        // Three distinct 6-Ks landed that day; Form 4 and Form 144 rows in the same feed are not covered.
        var alerts = await AlertsOfTypeAsync(AlertType.FilingLanded);
        alerts.Should().HaveCount(3).And.OnlyContain(a => a.Title == "6-K filed: GRAB");

        var fxAtome = alerts.Single(a => a.Message.Contains("000185561226000138", StringComparison.Ordinal));
        fxAtome.Message.Should().StartWith("GRAB filed a 6-K on 2026-09-15.");

        var outcome = await CaptureDeliverAndJudgeAsync(fxAtome, $"GRAB 6-K {GrabFxAtomeAccession}: financing update, noted.");

        outcome.Kind.Should().Be(nameof(CompanionEventKind.FilingLanded));
        outcome.Subject.Should().Be("GRAB");
        outcome.Outcome.Should().Be(EventOutcome.Verdict);

        // The next hourly re-read sees the same accessions and stays quiet.
        await RunFilingWatchAsync(GrabFilingDayRunUtc.AddHours(1));
        (await AlertsOfTypeAsync(AlertType.FilingLanded)).Should().HaveCount(3);
    }

    private async Task RunEarningsAheadAsync(DateTime nowUtc)
    {
        await using var alerts = AlertsContext();
        var calendar = new YahooEarningsCalendarService(
            new FixtureHttpClientFactory(), NullLogger<YahooEarningsCalendarService>.Instance);
        var job = new EarningsAheadJob(
            _banking.Object, Universe(), calendar, Generator(alerts), NullLogger<EarningsAheadJob>.Instance);

        await job.ExecuteAsync(nowUtc);
    }

    private async Task RunFilingWatchAsync(DateTime nowUtc)
    {
        await using var alerts = AlertsContext();
        var edgar = new SecEdgarService(new FixtureHttpClientFactory(), NullLogger<SecEdgarService>.Instance);
        var job = new FilingWatchJob(
            _banking.Object, Universe(), edgar, Generator(alerts), NullLogger<FilingWatchJob>.Instance);

        await job.ExecuteAsync(nowUtc);
    }

    /// <summary>
    /// Disposition → judgement → recorded outcome: companion capture picks the alert up as a pending
    /// event, the agent delivers it and records a notified verdict, and the day's outcome reads it back.
    /// </summary>
    private async Task<DailyEventOutcomeDto> CaptureDeliverAndJudgeAsync(
        Alert alert, string verdict)
    {
        await using var alerts = AlertsContext();
        await using var companion = CompanionContext();
        await using var agentCompanion = CompanionContext(_userId);
        await using var events = EventsContext(_userId);
        var outbox = new CompanionEventRepository(agentCompanion);

        var capture = new CompanionEventCapture(
            new MaterialAlertReader(alerts),
            _analystActions.Object,
            _brokerage.Object,
            _banking.Object,
            Mock.Of<IBankingAccountsReader>(),
            new NotificationSettingRepository(companion, Options.Create(new CompanionOptions())),
            new CompanionEventRepository(companion),
            new CompanionCaptureStateRepository(companion),
            _policy,
            Options.Create(new CompanionOptions()),
            NullLogger<CompanionEventCapture>.Instance);
        (await capture.CaptureAsync()).Should().BeGreaterThan(0);

        var dedupKey = _policy.AlertDedupKey(alert.Id);
        var evt = await agentCompanion.Events.AsNoTracking().SingleAsync(e => e.DedupKey == dedupKey);
        evt.Disposition.Should().Be(EventDisposition.Pending, "the default Scan mode queues look-ahead events for the agent");

        (await new AcknowledgeCompanionEventsCommandHandler(outbox)
            .Handle(new AcknowledgeCompanionEventsCommand(_userId, [evt.Id]), default)).Should().Be(1);

        var delivery = new EventsDeliveryAdapter(new OutboxDeliveryReader(outbox, _policy));
        var verdicts = new EventVerdictRepository(events);
        (await new RecordEventVerdictCommandHandler(delivery, verdicts)
            .Handle(new RecordEventVerdictCommand(_userId, evt.Id, verdict, Notified: true), default)).Should().BeTrue();

        var day = await new GetDailyEventOutcomesQueryHandler(delivery, verdicts).Handle(
            new GetDailyEventOutcomesQuery(_userId, DateOnly.FromDateTime(evt.OccurredAt.UtcDateTime)), default);

        var item = day.Items.Should().ContainSingle(i => i.EventId == evt.Id).Subject;
        item.AlertId.Should().Be(alert.Id);
        item.Verdict!.Text.Should().Be(verdict);
        return item;
    }

    private LookaheadUniverse Universe() =>
        new(_brokerage.Object, _watchlist.Object, _theses.Object, _crypto.Object);

    private AlertGeneratorService Generator(AlertsDbContext db) => new(new AlertRepository(db), _policyAcks.Object);

    private async Task<List<Alert>> AlertsOfTypeAsync(string type)
    {
        await using var read = AlertsContext(_userId);
        return await read.Alerts.AsNoTracking().Where(a => a.UserId == _userId && a.Type == type).ToListAsync();
    }

    // The jobs and the capture run with no person in scope (null), as they do in production; assertion reads act as the user.
    private AlertsDbContext AlertsContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<AlertsDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(actingUser));

    private CompanionDbContext CompanionContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<CompanionDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(actingUser));

    private EventsDbContext EventsContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<EventsDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(actingUser));

    /// <summary>Serves the captured provider responses; anything else is a 404, as an unknown ticker is live.</summary>
    private sealed class FixtureHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new FixtureHandler());
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        private static readonly Dictionary<string, string> Routes = new(StringComparer.Ordinal)
        {
            ["https://query1.finance.yahoo.com/v10/finance/quoteSummary/ACN"] = "yahoo-calendarEvents-ACN-2026-09.json",
            ["https://www.sec.gov/files/company_tickers.json"] = "sec-company_tickers-subset.json",
            ["https://data.sec.gov/submissions/CIK0001855612.json"] = "sec-submissions-GRAB-2026-09.json",
        };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.GetLeftPart(UriPartial.Path);
            if (url == "https://query1.finance.yahoo.com/v1/test/getcrumb")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("fixture-crumb") });
            }

            if (!Routes.TryGetValue(url, out var fixture))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(File.ReadAllText(Path.Combine(FixtureDir, fixture))),
            });
        }
    }
}
