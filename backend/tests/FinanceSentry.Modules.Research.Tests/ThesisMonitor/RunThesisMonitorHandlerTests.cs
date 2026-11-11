namespace FinanceSentry.Modules.Research.Tests.ThesisMonitor;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Fundamentals;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Domain.ThesisMonitor;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class RunThesisMonitorHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static FundamentalFact Fact(
        string concept, decimal value, int fiscalYear, string fiscalPeriod, DateOnly periodEnd)
        => new("MU", concept, concept, "USD", value, periodEnd, fiscalPeriod, fiscalYear, "10-Q");

    private static InvestmentThesis Thesis(IReadOnlyList<ThesisInvalidationTrigger> triggers) => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        Ticker = "MU",
        ThesisText = "Memory upcycle",
        InvalidationTriggers = triggers.ToList(),
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private static RunThesisMonitorCommandHandler BuildHandler(
        FakeThesisRepository repo,
        StubFundamentalsService fundamentals,
        FakeAlertGeneratorService alerts,
        FakeMarketDataService? marketData = null)
        => new(
            repo, fundamentals, marketData ?? new FakeMarketDataService(), alerts, new FakeThesisEventRecorder(),
            NullLogger<RunThesisMonitorCommandHandler>.Instance);

    [Fact]
    public async Task StillBroken_RaisesNoNewAlert_AndLeavesBrokenAtUnchanged()
    {
        var breachingFacts = new List<FundamentalFact>
        {
            Fact("GrossProfit", 30m, 2026, "Q2", new DateOnly(2026, 5, 31)),
            Fact("Revenue", 100m, 2026, "Q2", new DateOnly(2026, 5, 31)),
        };

        var trigger = new ThesisInvalidationTrigger(ThesisMetric.GrossMargin, "lessThan", 0.35m, ConsecutivePeriods: 1);
        var thesis = Thesis([trigger]);
        var brokenAt = DateTimeOffset.UtcNow.AddDays(-3);
        thesis.BrokenAt = brokenAt;
        thesis.BrokenReason = "gross_margin lessThan 0.35 — observed [0.30] over [2026Q2]";

        var repo = new FakeThesisRepository([thesis]);
        var fundamentals = new StubFundamentalsService(breachingFacts);
        var alerts = new FakeAlertGeneratorService();
        var handler = BuildHandler(repo, fundamentals, alerts);

        var summary = await handler.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        summary.BreaksRaised.Should().Be(0);
        summary.BreaksCleared.Should().Be(0);
        alerts.GenerateCalls.Should().Be(0);
        alerts.ResolveCalls.Should().Be(0);
        thesis.BrokenAt.Should().Be(brokenAt);
    }

    [Fact]
    public async Task ConditionCleared_ClearsBrokenState_AndResolvesAlert()
    {
        var healthyFacts = new List<FundamentalFact>
        {
            Fact("GrossProfit", 40m, 2026, "Q2", new DateOnly(2026, 5, 31)),
            Fact("Revenue", 100m, 2026, "Q2", new DateOnly(2026, 5, 31)),
        };

        var trigger = new ThesisInvalidationTrigger(ThesisMetric.GrossMargin, "lessThan", 0.35m, ConsecutivePeriods: 1);
        var thesis = Thesis([trigger]);
        thesis.BrokenAt = DateTimeOffset.UtcNow.AddDays(-3);
        thesis.BrokenReason = "gross_margin lessThan 0.35 — observed [0.30] over [2026Q1]";

        var repo = new FakeThesisRepository([thesis]);
        var fundamentals = new StubFundamentalsService(healthyFacts);
        var alerts = new FakeAlertGeneratorService();
        var handler = BuildHandler(repo, fundamentals, alerts);

        var summary = await handler.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        summary.BreaksCleared.Should().Be(1);
        summary.BreaksRaised.Should().Be(0);
        alerts.ResolveCalls.Should().Be(1);
        thesis.BrokenAt.Should().BeNull();
        thesis.BrokenReason.Should().BeNull();
    }

    [Fact]
    public async Task ReBreachAfterClear_RaisesFreshBreakAndAlert()
    {
        var breachingFacts = new List<FundamentalFact>
        {
            Fact("GrossProfit", 30m, 2026, "Q2", new DateOnly(2026, 5, 31)),
            Fact("Revenue", 100m, 2026, "Q2", new DateOnly(2026, 5, 31)),
        };

        var trigger = new ThesisInvalidationTrigger(ThesisMetric.GrossMargin, "lessThan", 0.35m, ConsecutivePeriods: 1);
        var thesis = Thesis([trigger]);
        thesis.BrokenAt = null;
        thesis.BrokenReason = null;

        var repo = new FakeThesisRepository([thesis]);
        var fundamentals = new StubFundamentalsService(breachingFacts);
        var alerts = new FakeAlertGeneratorService();
        var handler = BuildHandler(repo, fundamentals, alerts);

        var summary = await handler.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        summary.BreaksRaised.Should().Be(1);
        alerts.GenerateCalls.Should().Be(1);
        thesis.BrokenAt.Should().NotBeNull();
        thesis.BrokenReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task NoFundamentals_NeverBreaks_CountsAsSkipped()
    {
        var trigger = new ThesisInvalidationTrigger(ThesisMetric.GrossMargin, "lessThan", 0.35m, ConsecutivePeriods: 1);
        var thesis = Thesis([trigger]);

        var repo = new FakeThesisRepository([thesis]);
        var fundamentals = new StubFundamentalsService([]); // no EDGAR filer for this ticker
        var alerts = new FakeAlertGeneratorService();
        var handler = BuildHandler(repo, fundamentals, alerts);

        var summary = await handler.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        summary.BreaksRaised.Should().Be(0);
        summary.Skipped.Should().Be(1);
        thesis.BrokenAt.Should().BeNull();
    }

    [Fact]
    public async Task NoSourceForAnyTrigger_ReportsTheThesisUnmonitorable_WithItsCoverage()
    {
        // #837: a thesis whose triggers have no data must say so, never read as quietly intact.
        var trigger = new ThesisInvalidationTrigger(ThesisMetric.OperatingMargin, "lessThan", 0m, ConsecutivePeriods: 2);
        var thesis = Thesis([trigger]);
        var coverage = new FundamentalsCoverage(
            FundamentalsCoverageStatus.NoSourceAvailable, FundamentalsIssuerType.NotSecRegistrant,
            "sec-edgar-us-gaap: not in the SEC ticker map; yahoo-finance: no fundamentals timeseries", []);

        var repo = new FakeThesisRepository([thesis]);
        var handler = BuildHandler(repo, new StubFundamentalsService([], coverage: coverage), new FakeAlertGeneratorService());

        var summary = await handler.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        var blind = summary.Unmonitorable.Should().ContainSingle().Subject;
        blind.ThesisId.Should().Be(thesis.Id);
        blind.Status.Should().Be(ThesisMonitorability.Unmonitorable);
        blind.Triggers.Should().ContainSingle().Which.Should().BeEquivalentTo(new UnmonitorableTrigger(
            ThesisMetric.OperatingMargin, FundamentalsBasis.Quarterly, "MU", NonEvaluableReason.NoFundamentals, coverage));
        thesis.BrokenAt.Should().BeNull();
    }

    [Fact]
    public async Task OneBlindTrigger_ReportsThesisPartiallyMonitorable_ListingOnlyThatTrigger()
    {
        // GRAB's shape: five quarters evaluate an operating-margin trigger, but a two-quarter revenue
        // YoY trigger needs six (two prior-year pairs).
        var facts = new List<FundamentalFact>();
        foreach (var end in new[] { new DateOnly(2026, 6, 30), new DateOnly(2026, 3, 31), new DateOnly(2025, 12, 31),
                     new DateOnly(2025, 9, 30), new DateOnly(2025, 6, 30) })
        {
            facts.Add(new FundamentalFact("MU", "Revenue", "Revenue", "USD", 1000m, end, "3M", null, string.Empty));
            facts.Add(new FundamentalFact("MU", "OperatingIncome", "OperatingIncome", "USD", 90m, end, "3M", null, string.Empty));
        }

        var margin = new ThesisInvalidationTrigger(ThesisMetric.OperatingMargin, "lessThan", 0m, ConsecutivePeriods: 2);
        var growth = new ThesisInvalidationTrigger(ThesisMetric.RevenueYoy, "lessThan", 0.10m, ConsecutivePeriods: 2);
        var thesis = Thesis([margin, growth]);

        var repo = new FakeThesisRepository([thesis]);
        var handler = BuildHandler(repo, new StubFundamentalsService(facts), new FakeAlertGeneratorService());

        var summary = await handler.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        var blind = summary.Unmonitorable.Should().ContainSingle().Subject;
        blind.Status.Should().Be(ThesisMonitorability.PartiallyMonitorable);
        blind.Triggers.Should().ContainSingle().Which.Should().Match<UnmonitorableTrigger>(t =>
            t.Metric == ThesisMetric.RevenueYoy && t.Reason == NonEvaluableReason.InsufficientPeriods
            && t.Coverage != null && t.Coverage.Status == FundamentalsCoverageStatus.Covered);
        summary.Skipped.Should().Be(0);
    }

    [Fact]
    public async Task EveryTriggerEvaluated_ReportsNothingUnmonitorable()
    {
        var facts = new List<FundamentalFact>
        {
            Fact("GrossProfit", 40m, 2026, "Q2", new DateOnly(2026, 5, 31)),
            Fact("Revenue", 100m, 2026, "Q2", new DateOnly(2026, 5, 31)),
        };
        var trigger = new ThesisInvalidationTrigger(ThesisMetric.GrossMargin, "lessThan", 0.35m, ConsecutivePeriods: 1);

        var repo = new FakeThesisRepository([Thesis([trigger])]);
        var handler = BuildHandler(repo, new StubFundamentalsService(facts), new FakeAlertGeneratorService());

        var summary = await handler.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        summary.Unmonitorable.Should().BeEmpty();
    }

    [Fact]
    public async Task InsufficientPeriods_AllNonEvaluable_NeverBreaks()
    {
        var oneFact = new List<FundamentalFact>
        {
            Fact("Revenue", 100m, 2026, "Q2", new DateOnly(2026, 5, 31)),
        };

        // revenue_yoy needs a prior-year matching fiscal period, which is absent here.
        var trigger = new ThesisInvalidationTrigger(ThesisMetric.RevenueYoy, "lessThan", 0m, ConsecutivePeriods: 1);
        var thesis = Thesis([trigger]);

        var repo = new FakeThesisRepository([thesis]);
        var fundamentals = new StubFundamentalsService(oneFact);
        var alerts = new FakeAlertGeneratorService();
        var handler = BuildHandler(repo, fundamentals, alerts);

        var summary = await handler.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        summary.BreaksRaised.Should().Be(0);
        summary.Skipped.Should().Be(1);
        thesis.BrokenAt.Should().BeNull();
    }

    [Fact]
    public async Task FetchFailureForOneTicker_CountsAsError_RunContinuesForOthers()
    {
        var trigger = new ThesisInvalidationTrigger(ThesisMetric.Revenue, "lessThan", 10m, ConsecutivePeriods: 1);
        var failingThesis = Thesis([trigger]);
        failingThesis.Ticker = "BOOM";

        var healthyFacts = new List<FundamentalFact>
        {
            Fact("Revenue", 100m, 2026, "Q2", new DateOnly(2026, 5, 31)),
        };
        var okThesis = Thesis([trigger]);

        var repo = new FakeThesisRepository([failingThesis, okThesis]);
        var fundamentals = new StubFundamentalsService(healthyFacts, throwForTicker: "BOOM");
        var alerts = new FakeAlertGeneratorService();
        var handler = BuildHandler(repo, fundamentals, alerts);

        var summary = await handler.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        summary.Errors.Should().Be(1);
        summary.ThesesEvaluated.Should().Be(2);
    }

    // ── Bug 2 regression: clearing all triggers must resolve orphaned breaks ──

    [Fact]
    public async Task ZeroTriggers_BrokenThesis_ClearsBreakAndResolvesAlert()
    {
        // Thesis was broken, then all triggers were removed. The next monitor run must clear the break.
        var thesis = Thesis([]);
        thesis.BrokenAt = DateTimeOffset.UtcNow.AddDays(-5);
        thesis.BrokenReason = "gross_margin lessThan 0.35 — observed [0.30] over [2026Q1]";

        var repo = new FakeThesisRepository([thesis]);
        var alerts = new FakeAlertGeneratorService();
        var handler = BuildHandler(repo, new StubFundamentalsService([]), alerts);

        var summary = await handler.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        summary.BreaksCleared.Should().Be(1);
        summary.BreaksRaised.Should().Be(0);
        alerts.ResolveCalls.Should().Be(1);
        thesis.BrokenAt.Should().BeNull();
        thesis.BrokenReason.Should().BeNull();
    }

    [Fact]
    public async Task ZeroTriggers_NotBrokenThesis_SkipsWithNoSideEffects()
    {
        // Thesis was never broken and has no triggers — should be silently skipped.
        var thesis = Thesis([]);

        var repo = new FakeThesisRepository([thesis]);
        var alerts = new FakeAlertGeneratorService();
        var handler = BuildHandler(repo, new StubFundamentalsService([]), alerts);

        var summary = await handler.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        summary.BreaksCleared.Should().Be(0);
        summary.BreaksRaised.Should().Be(0);
        summary.Skipped.Should().Be(1);
        alerts.ResolveCalls.Should().Be(0);
        thesis.BrokenAt.Should().BeNull();
    }

    [Fact]
    public async Task RelativeReturn_SustainedUnderperformanceVsBenchmark_BreaksThesis()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var day0 = today.AddDays(-3);
        var day1 = today.AddDays(-2);
        var day2 = today.AddDays(-1);
        var day3 = today;

        var subjectCloses = new List<DailyClose>
        {
            new(day0, 100m),
            new(day1, 90m),
            new(day2, 80m),
            new(day3, 70m),
        };
        var benchmarkCloses = new List<DailyClose>
        {
            new(day0, 100m),
            new(day1, 100m),
            new(day2, 100m),
            new(day3, 100m),
        };

        var trigger = new ThesisInvalidationTrigger(
            ThesisMetric.RelativeReturn, "lessThan", -0.05m, BenchmarkTicker: "SPY", WindowDays: 1, ConsecutivePeriods: 2);
        var thesis = Thesis([trigger]);

        var repo = new FakeThesisRepository([thesis]);
        var alerts = new FakeAlertGeneratorService();
        var marketData = new FakeMarketDataService(new Dictionary<string, IReadOnlyList<DailyClose>>
        {
            ["MU"] = subjectCloses,
            ["SPY"] = benchmarkCloses,
        });
        var handler = BuildHandler(repo, new StubFundamentalsService([]), alerts, marketData);

        var summary = await handler.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        summary.BreaksRaised.Should().Be(1);
        alerts.GenerateCalls.Should().Be(1);
        thesis.BrokenAt.Should().NotBeNull();
        thesis.BrokenReason.Should().Contain(ThesisMetric.RelativeReturn);
    }


    // ── #1375: infra-blind marking, post-run meta-check, coverage-degradation diff ───────────────

    private static readonly FundamentalsCoverage MapDownCoverage = new(
        FundamentalsCoverageStatus.SourceUnavailable, null,
        "sec-edgar-us-gaap unavailable: EDGAR ticker->CIK map unavailable", [], EdgarErrorCodes.MapUnavailable);

    private static readonly ThesisInvalidationTrigger GrossMarginTrigger =
        new(ThesisMetric.GrossMargin, "lessThan", 0.35m, ConsecutivePeriods: 1);

    private static List<FundamentalFact> HealthyGrossMarginFacts() =>
    [
        Fact("GrossProfit", 40m, 2026, "Q2", new DateOnly(2026, 5, 31)),
        Fact("Revenue", 100m, 2026, "Q2", new DateOnly(2026, 5, 31)),
    ];

    [Fact]
    public async Task SourceUnavailable_MarksTheTriggerInfraBlind_NotNoData()
    {
        var thesis = Thesis([GrossMarginTrigger]);
        var handler = BuildHandler(
            new FakeThesisRepository([thesis]), new StubFundamentalsService([], coverage: MapDownCoverage),
            new FakeAlertGeneratorService());

        var summary = await handler.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        var trigger = summary.Unmonitorable.Single().Triggers.Single();
        trigger.Cause.Should().Be(ThesisBlindCause.InfraBlind);
        trigger.Coverage!.ErrorCode.Should().Be(EdgarErrorCodes.MapUnavailable);
    }

    [Fact]
    public async Task NoSourceAvailable_StaysNoData_NotInfraBlind()
    {
        var coverage = new FundamentalsCoverage(
            FundamentalsCoverageStatus.NoSourceAvailable, FundamentalsIssuerType.NotSecRegistrant, "no data", []);
        var handler = BuildHandler(
            new FakeThesisRepository([Thesis([GrossMarginTrigger])]), new StubFundamentalsService([], coverage: coverage),
            new FakeAlertGeneratorService());

        var summary = await handler.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        summary.Unmonitorable.Single().Triggers.Single().Cause.Should().Be(ThesisBlindCause.NoData);
    }

    [Fact]
    public async Task PriceTriggerWithNoHistory_IsNoData_NotInfraBlind()
    {
        var trigger = new ThesisInvalidationTrigger(ThesisMetric.PriceDrawdown, "lessThan", -0.3m);
        var handler = BuildHandler(
            new FakeThesisRepository([Thesis([trigger])]), new StubFundamentalsService([]), new FakeAlertGeneratorService());

        var summary = await handler.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        summary.Unmonitorable.Single().Triggers.Single().Cause.Should().Be(ThesisBlindCause.NoData);
    }

    [Fact]
    public async Task BlindThesis_RaisesAnInfraBlindAlertAfterTheRun_NamingTheTriggerAndCause()
    {
        var alerts = new FakeAlertGeneratorService();
        var handler = BuildHandler(
            new FakeThesisRepository([Thesis([GrossMarginTrigger])]),
            new StubFundamentalsService([], coverage: MapDownCoverage), alerts);

        await handler.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        var (summary, infraBlind) = alerts.BlindAlerts.Should().ContainSingle().Subject;
        infraBlind.Should().BeTrue();
        summary.Should().Contain("MU").And.Contain(ThesisMetric.GrossMargin).And.Contain("INFRA_BLIND")
            .And.Contain("ticker->CIK map unavailable").And.Contain(ThesisMonitorability.Unmonitorable);
        alerts.BlindResolveCalls.Should().Be(0);
    }

    [Fact]
    public async Task PartiallyMonitorableThesis_WithoutAnOutage_AlertsAsWarningNotInfra()
    {
        // Only the margin trigger has the periods it needs; revenue YoY has no prior-year pair.
        var facts = new List<FundamentalFact>
        {
            Fact("Revenue", 100m, 2026, "Q2", new DateOnly(2026, 5, 31)),
            Fact("GrossProfit", 40m, 2026, "Q2", new DateOnly(2026, 5, 31)),
        };
        var growth = new ThesisInvalidationTrigger(ThesisMetric.RevenueYoy, "lessThan", 0m, ConsecutivePeriods: 1);
        var alerts = new FakeAlertGeneratorService();
        var handler = BuildHandler(
            new FakeThesisRepository([Thesis([GrossMarginTrigger, growth])]), new StubFundamentalsService(facts), alerts);

        await handler.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        var (summary, infraBlind) = alerts.BlindAlerts.Should().ContainSingle().Subject;
        infraBlind.Should().BeFalse();
        summary.Should().Contain(ThesisMetric.RevenueYoy).And.Contain(ThesisMonitorability.PartiallyMonitorable)
            .And.NotContain(ThesisMetric.GrossMargin);
    }

    [Fact]
    public async Task FullyMonitorableRun_RaisesNoBlindAlert_AndResolvesTheOpenOne()
    {
        var alerts = new FakeAlertGeneratorService();
        var handler = BuildHandler(
            new FakeThesisRepository([Thesis([GrossMarginTrigger])]), new StubFundamentalsService(HealthyGrossMarginFacts()), alerts);

        await handler.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        alerts.BlindAlerts.Should().BeEmpty();
        alerts.BlindResolveCalls.Should().Be(1);
    }

    [Fact]
    public async Task RunThatErrored_DoesNotResolveTheBlindAlert()
    {
        var alerts = new FakeAlertGeneratorService();
        var handler = BuildHandler(
            new FakeThesisRepository([Thesis([GrossMarginTrigger])]),
            new StubFundamentalsService(HealthyGrossMarginFacts(), throwForTicker: "MU"), alerts);

        var summary = await handler.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        summary.Errors.Should().Be(1);
        alerts.BlindResolveCalls.Should().Be(0);
    }

    [Fact]
    public async Task AlertFailure_NeverAbortsTheRun()
    {
        var alerts = new FakeAlertGeneratorService { BlindAlertThrows = true };
        var handler = BuildHandler(
            new FakeThesisRepository([Thesis([GrossMarginTrigger])]),
            new StubFundamentalsService([], coverage: MapDownCoverage), alerts);

        var summary = await handler.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        summary.Unmonitorable.Should().ContainSingle();
    }

    [Fact]
    public async Task EveryRun_StoresTheEvaluableTriggerCount()
    {
        var thesis = Thesis([GrossMarginTrigger, new ThesisInvalidationTrigger(ThesisMetric.RevenueYoy, "lessThan", 0m)]);
        var repo = new FakeThesisRepository([thesis]);
        var handler = BuildHandler(repo, new StubFundamentalsService(HealthyGrossMarginFacts()), new FakeAlertGeneratorService());

        await handler.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        var snapshot = repo.Snapshots.Should().ContainSingle().Subject;
        snapshot.TotalTriggers.Should().Be(2);
        snapshot.EvaluableKeys.Should().ContainSingle().Which.Should().StartWith(ThesisMetric.GrossMargin + "|MU|");
    }

    [Fact]
    public async Task FirstMonitoredRun_HasNothingToDiff_RaisesNoDegradationAlert()
    {
        var alerts = new FakeAlertGeneratorService();
        var handler = BuildHandler(
            new FakeThesisRepository([Thesis([GrossMarginTrigger])]),
            new StubFundamentalsService([], coverage: MapDownCoverage), alerts);

        var summary = await handler.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        alerts.DegradedAlerts.Should().BeEmpty();
        summary.CoverageDegraded.Should().BeEmpty();
    }

    [Fact]
    public async Task TriggerThatEvaluatedLastRunAndFailsNow_RaisesACoverageDegradationAlert_OnceUntilItRecovers()
    {
        var thesis = Thesis([GrossMarginTrigger]);
        var repo = new FakeThesisRepository([thesis]);
        var alerts = new FakeAlertGeneratorService();

        // Run 1: the map is up and the trigger evaluates.
        await BuildHandler(repo, new StubFundamentalsService(HealthyGrossMarginFacts()), alerts)
            .Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);
        alerts.DegradedAlerts.Should().BeEmpty();

        // Run 2: the map is down - the trigger went blind since this run.
        var down = BuildHandler(repo, new StubFundamentalsService([], coverage: MapDownCoverage), alerts);
        var second = await down.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        var (thesisId, ticker, reason) = alerts.DegradedAlerts.Should().ContainSingle().Subject;
        thesisId.Should().Be(thesis.Id);
        ticker.Should().Be("MU");
        reason.Should().Contain("MU gross_margin went blind since").And.Contain("INFRA_BLIND");
        second.CoverageDegraded.Should().ContainSingle().Which.Triggers.Should().ContainSingle()
            .Which.Metric.Should().Be(ThesisMetric.GrossMargin);

        // Run 3: still down - already blind last run, so not reported as a fresh degradation.
        await down.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);
        alerts.DegradedAlerts.Should().HaveCount(1);
        alerts.BlindAlerts.Should().HaveCount(2); // the blind-spot notice repeats every run; the alert store dedups it

        // Run 4: recovered; run 5: down again - a new regression, a new alert.
        await BuildHandler(repo, new StubFundamentalsService(HealthyGrossMarginFacts()), alerts)
            .Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);
        await down.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);
        alerts.DegradedAlerts.Should().HaveCount(2);
    }

    [Fact]
    public async Task EditingATriggerThreshold_IsNotACoverageChange()
    {
        var thesis = Thesis([GrossMarginTrigger]);
        var repo = new FakeThesisRepository([thesis]);
        var alerts = new FakeAlertGeneratorService();
        await BuildHandler(repo, new StubFundamentalsService(HealthyGrossMarginFacts()), alerts)
            .Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        thesis.InvalidationTriggers = [GrossMarginTrigger with { Threshold = 0.20m }];
        await BuildHandler(repo, new StubFundamentalsService(HealthyGrossMarginFacts()), alerts)
            .Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        alerts.DegradedAlerts.Should().BeEmpty();
    }

    [Fact]
    public async Task OnlyTheTriggerThatWentBlind_IsNamedInTheDegradation()
    {
        // Gross margin keeps evaluating; revenue YoY (which needs a prior-year pair) was evaluable and is not now.
        var yoy = new ThesisInvalidationTrigger(ThesisMetric.RevenueYoy, "lessThan", 0m, ConsecutivePeriods: 1);
        var thesis = Thesis([GrossMarginTrigger, yoy]);
        var repo = new FakeThesisRepository([thesis]);
        var alerts = new FakeAlertGeneratorService();

        var withPriorYear = HealthyGrossMarginFacts();
        withPriorYear.Add(Fact("Revenue", 90m, 2025, "Q2", new DateOnly(2025, 5, 31)));
        await BuildHandler(repo, new StubFundamentalsService(withPriorYear), alerts)
            .Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        var summary = await BuildHandler(repo, new StubFundamentalsService(HealthyGrossMarginFacts()), alerts)
            .Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        var degradation = summary.CoverageDegraded.Should().ContainSingle().Subject;
        degradation.Triggers.Should().ContainSingle().Which.Metric.Should().Be(ThesisMetric.RevenueYoy);
        alerts.DegradedAlerts.Single().Reason.Should().Contain("MU revenue_yoy").And.NotContain("gross_margin");
    }

    [Fact]
    public async Task SnapshotWriteFailure_NeverAbortsTheRun()
    {
        var repo = new FakeThesisRepository([Thesis([GrossMarginTrigger])]) { SnapshotWritesFail = true };
        var handler = BuildHandler(repo, new StubFundamentalsService(HealthyGrossMarginFacts()), new FakeAlertGeneratorService());

        var summary = await handler.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        summary.Errors.Should().Be(0);
        summary.ThesesEvaluated.Should().Be(1);
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeThesisRepository(IReadOnlyList<InvestmentThesis> theses) : IThesisRepository
    {
        public Task<IReadOnlyList<InvestmentThesis>> ListAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(theses);

        public Task<IReadOnlyList<InvestmentThesis>> ListUnscopedAsync(Guid userId, CancellationToken ct = default)
            => ListAsync(userId, ct);

        public Task<IReadOnlyList<Guid>> GetUserIdsWithThesesUnscopedAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>([UserId]);

        public Task<InvestmentThesis?> FindAsync(Guid userId, Guid id, CancellationToken ct = default)
            => Task.FromResult(theses.FirstOrDefault(t => t.Id == id));

        public Task<IReadOnlyList<InvestmentThesis>> FindByTickerAsync(Guid userId, string ticker, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<InvestmentThesis>>(theses.Where(t => t.Ticker == ticker).ToList());

        public Task UpsertAsync(InvestmentThesis thesis, CancellationToken ct = default) => Task.CompletedTask;

        // Persistence stand-in: the next run reads the snapshot back off the same thesis instance.
        public List<ThesisMonitorSnapshot> Snapshots { get; } = [];

        public bool SnapshotWritesFail { get; set; }

        public Task SaveMonitorSnapshotAsync(
            Guid userId, Guid thesisId, ThesisMonitorSnapshot snapshot, CancellationToken ct = default)
        {
            if (SnapshotWritesFail)
            {
                throw new InvalidOperationException("snapshot write failed");
            }

            Snapshots.Add(snapshot);
            theses.Single(t => t.Id == thesisId).MonitorSnapshot = snapshot;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class StubFundamentalsService(
        IReadOnlyList<FundamentalFact> facts,
        string? throwForTicker = null,
        FundamentalsCoverage? coverage = null) : IFundamentalsService
    {
        public Task<FundamentalsResult> GetFundamentalsAsync(
            string ticker, int maxPerConcept, CancellationToken ct = default)
        {
            if (throwForTicker is not null && string.Equals(ticker, throwForTicker, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Fundamentals fetch failed for {ticker}");
            }

            var result = FakeFundamentalsService.ResultFor(ticker, facts);
            return Task.FromResult(coverage is null ? result : result with { Coverage = coverage });
        }
    }

    private sealed class FakeMarketDataService(
        IReadOnlyDictionary<string, IReadOnlyList<DailyClose>>? closesByTicker = null) : IMarketDataService
    {
        public Task<IReadOnlyDictionary<string, QuoteCacheEntry>> GetQuotesAsync(
            IReadOnlyCollection<string> tickers, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, QuoteCacheEntry>>(
                new Dictionary<string, QuoteCacheEntry>());

        public Task<IReadOnlyList<DailyClose>> GetDailyClosesAsync(
            string ticker, DateOnly since, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DailyClose>>(
                closesByTicker is not null && closesByTicker.TryGetValue(ticker, out var closes)
                    ? closes.Where(c => c.Date >= since).ToList()
                    : []);
    }

    private sealed class FakeThesisEventRecorder : IThesisEventRecorder
    {
        public Task RecordAsync(
            Guid userId,
            ThesisSubjectType subjectType,
            Guid subjectId,
            string ticker,
            ThesisEventType eventType,
            string? decisionNote = null,
            CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeAlertGeneratorService : IAlertGeneratorService
    {
        public int GenerateCalls { get; private set; }
        public int ResolveCalls { get; private set; }

        public List<(string Summary, bool InfraBlind)> BlindAlerts { get; } = [];
        public int BlindResolveCalls { get; private set; }
        public List<(Guid ThesisId, string Ticker, string Reason)> DegradedAlerts { get; } = [];
        public bool BlindAlertThrows { get; set; }

        public Task GenerateThesisMonitoringBlindAlertAsync(
            Guid userId, string summary, bool infraBlind, CancellationToken ct = default)
        {
            if (BlindAlertThrows)
            {
                throw new InvalidOperationException("alert store down");
            }

            BlindAlerts.Add((summary, infraBlind));
            return Task.CompletedTask;
        }

        public Task ResolveThesisMonitoringBlindAlertAsync(Guid userId, CancellationToken ct = default)
        {
            BlindResolveCalls++;
            return Task.CompletedTask;
        }

        public Task GenerateThesisCoverageDegradedAlertAsync(
            Guid userId, Guid thesisId, string ticker, string reason, CancellationToken ct = default)
        {
            DegradedAlerts.Add((thesisId, ticker, reason));
            return Task.CompletedTask;
        }

        public Task GenerateLowBalanceAlertAsync(
            Guid userId, Guid accountId, string accountName, decimal balance, decimal threshold, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ResolveLowBalanceAlertAsync(Guid userId, Guid accountId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateSyncFailureAlertAsync(
            Guid userId, string provider, Guid? accountId, string? accountName, string? errorCode, SyncFailureClass failureClass, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task EscalateSyncFailureAlertAsync(
            Guid userId, string provider, Guid? accountId, string? accountName, string? errorCode, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateConnectionDormantAlertAsync(
            Guid userId, string provider, Guid? accountId, string? accountName, TimeSpan failingFor, bool othersSyncing,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ResolveConnectionDormantAlertAsync(
            Guid userId, string provider, Guid? accountId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ResolveSyncFailureAlertAsync(Guid userId, string provider, Guid? accountId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateAccountRetiredAlertAsync(Guid userId, Guid accountId, string accountName, string? replacedBy, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateCoverageGapAlertAsync(
            Guid userId, Guid accountId, string accountName, int quietWeeks, decimal baselineWeeklySpendUsd,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ResolveCoverageGapAlertAsync(Guid userId, Guid accountId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteAlertsForAccountAsync(Guid accountId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateConsentExpiringAlertAsync(
            Guid userId, Guid referenceId, string providerName, DateTime expiresAt, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateThesisBreakAlertAsync(
            Guid userId, Guid thesisId, string ticker, string reason, CancellationToken ct = default)
        {
            GenerateCalls++;
            return Task.CompletedTask;
        }

        public Task ResolveThesisBreakAlertAsync(Guid userId, Guid thesisId, CancellationToken ct = default)
        {
            ResolveCalls++;
            return Task.CompletedTask;
        }

        public Task GenerateMarketStructureAlertAsync(
            Guid userId, Guid referenceId, string ticker, string reason, CancellationToken ct = default,
            AlertDedup dedup = AlertDedup.ActiveThenSilence)
            => Task.CompletedTask;

        public Task GenerateMarketStructureFreshnessAlertAsync(
            Guid userId, Guid referenceId, string reason, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GeneratePolicyViolationAlertAsync(
            Guid userId, string ruleKey, string subject, decimal observedValue, decimal limitValue,
            bool isOverride = false, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ResolvePolicyViolationAlertAsync(
            Guid userId, string ruleKey, string subject, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateOpportunityAlertAsync(
            Guid userId, Guid referenceId, string ticker, string reason, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GeneratePerformanceBriefAlertAsync(Guid userId, string headline, string body, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateCashShortfallAlertAsync(
            Guid userId, Guid accountId, string accountName,
            DateOnly shortfallDate, decimal shortfallAmount, string currency, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ResolveCashShortfallAlertAsync(Guid userId, Guid accountId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GeneratePriceHikeAlertAsync(
            Guid userId, Guid subscriptionId, string merchantName,
            decimal oldAmount, decimal newAmount, string currency, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateDuplicateChargeAlertAsync(
            Guid userId, Guid accountId, string merchantKey, string merchantName, decimal chargeAmount,
            string currency, int chargeCount, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateCategorySpikeAlertAsync(
            Guid userId, string category, decimal currentMonthSpend, decimal baselineSpend,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateFxSpreadAlertAsync(
            Guid userId, Guid debitTransactionId, string fromCurrency, string toCurrency,
            decimal impliedRate, decimal marketRate, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateRebalanceProposalAlertAsync(Guid userId, int orderCount, string orderSummary, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateCashSweepProposalAlertAsync(Guid userId, decimal idleCashUsd, decimal minBufferUsd, decimal excessUsd, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GeneratePolicyReviewAlertAsync(Guid userId, Guid reviewId, int adjustmentCount, string summary, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GeneratePolicyReviewMissedAlertAsync(Guid userId, DateTimeOffset dueAt, int daysOverdue, string cadence, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateEarningsAheadAlertAsync(
            Guid userId, string ticker, string eventType, DateOnly eventDate, bool isEstimate,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateFilingLandedAlertAsync(
            Guid userId, string ticker, string form, DateOnly filingDate, string accessionNumber, string documentUrl,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateAnalystRatingChangeAlertAsync(
            Guid userId, string ticker, DateOnly day, IReadOnlyList<AnalystRatingChange> changes,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateNewsClusterAlertAsync(
            Guid userId, string ticker, string reason, DateOnly day, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateBudgetNearLimitAlertAsync(
            Guid userId, Guid budgetId, string category, decimal spentUsd, decimal limitUsd, int year, int month,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateBudgetExceededAlertAsync(
            Guid userId, Guid budgetId, string category, decimal spentUsd, decimal limitUsd, int year, int month,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateBudgetPaceAlertAsync(
            Guid userId, Guid budgetId, string category, decimal spentUsd, decimal limitUsd,
            decimal projectedMonthEndSpendUsd, int year, int month, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateBudgetTotalPaceAlertAsync(
            Guid userId, decimal estimateUsd, decimal expectedMonthEndUsd, decimal spentUsd,
            int year, int month, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateFamilyStatementAlertAsync(
            Guid userId, string headline, string body, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateFireBriefAlertAsync(
            Guid userId, string headline, string body, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateRelativeUnderperformanceAlertAsync(
            Guid userId, string scope, string scopeKey, string label, string window, string benchmarkTicker,
            decimal excessReturnPct, int runs, decimal thresholdPct, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ResolveRelativeUnderperformanceAlertAsync(
            Guid userId, string scope, string scopeKey, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ResolveOpportunityAlertAsync(Guid userId, Guid referenceId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ResolveConsentExpiringAlertAsync(Guid userId, Guid referenceId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateDetectorSilentAlertAsync(
            Guid userId, string detectorName, int silentDays, int inputCount, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ResolveDetectorSilentAlertAsync(Guid userId, string detectorName, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ResolveMarketStructureFreshnessAlertAsync(Guid userId, Guid referenceId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ResolveCategorySpikeAlertAsync(Guid userId, string category, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ResolveRebalanceProposalAlertAsync(Guid userId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ResolveCashSweepProposalAlertAsync(Guid userId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ResolveEarningsAheadAlertAsync(
            Guid userId, string ticker, string eventType, DateOnly eventDate, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateMacroEventAheadAlertAsync(
            Guid userId, string eventKey, string eventName, string region, DateTimeOffset eventAt, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ResolveMacroEventAheadAlertAsync(Guid userId, string eventKey, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateMacroEventReleasedAlertAsync(
            Guid userId, string eventKey, string eventName, string region, DateTimeOffset eventAt, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
