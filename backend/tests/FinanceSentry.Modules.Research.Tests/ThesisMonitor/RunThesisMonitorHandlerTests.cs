namespace FinanceSentry.Modules.Research.Tests.ThesisMonitor;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
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
        FakeThesisRepository repo, FakeSecEdgarService secEdgar, FakeAlertGeneratorService alerts)
        => new(
            repo, secEdgar, new FakeMarketDataService(), alerts, new FakeThesisEventRecorder(),
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
        var secEdgar = new FakeSecEdgarService(breachingFacts);
        var alerts = new FakeAlertGeneratorService();
        var handler = BuildHandler(repo, secEdgar, alerts);

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
        var secEdgar = new FakeSecEdgarService(healthyFacts);
        var alerts = new FakeAlertGeneratorService();
        var handler = BuildHandler(repo, secEdgar, alerts);

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
        var secEdgar = new FakeSecEdgarService(breachingFacts);
        var alerts = new FakeAlertGeneratorService();
        var handler = BuildHandler(repo, secEdgar, alerts);

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
        var secEdgar = new FakeSecEdgarService([]); // no EDGAR filer for this ticker
        var alerts = new FakeAlertGeneratorService();
        var handler = BuildHandler(repo, secEdgar, alerts);

        var summary = await handler.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        summary.BreaksRaised.Should().Be(0);
        summary.Skipped.Should().Be(1);
        thesis.BrokenAt.Should().BeNull();
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
        var secEdgar = new FakeSecEdgarService(oneFact);
        var alerts = new FakeAlertGeneratorService();
        var handler = BuildHandler(repo, secEdgar, alerts);

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
        var secEdgar = new FakeSecEdgarService(healthyFacts, throwForTicker: "BOOM");
        var alerts = new FakeAlertGeneratorService();
        var handler = BuildHandler(repo, secEdgar, alerts);

        var summary = await handler.Handle(new RunThesisMonitorCommand(UserId), CancellationToken.None);

        summary.Errors.Should().Be(1);
        summary.ThesesEvaluated.Should().Be(2);
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeThesisRepository(IReadOnlyList<InvestmentThesis> theses) : IThesisRepository
    {
        public Task<IReadOnlyList<InvestmentThesis>> ListAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(theses);

        public Task<IReadOnlyList<Guid>> GetUserIdsWithThesesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>([UserId]);

        public Task<InvestmentThesis?> FindAsync(Guid userId, Guid id, CancellationToken ct = default)
            => Task.FromResult(theses.FirstOrDefault(t => t.Id == id));

        public Task<IReadOnlyList<InvestmentThesis>> FindByTickerAsync(Guid userId, string ticker, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<InvestmentThesis>>(theses.Where(t => t.Ticker == ticker).ToList());

        public Task UpsertAsync(InvestmentThesis thesis, CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class FakeSecEdgarService(IReadOnlyList<FundamentalFact> facts, string? throwForTicker = null)
        : ISecEdgarService
    {
        public Task<IReadOnlyList<EdgarFiling>> GetRecentFilingsAsync(
            string ticker, IReadOnlyCollection<string>? formTypes, int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EdgarFiling>>([]);

        public Task<IReadOnlyList<FundamentalFact>> GetFundamentalsAsync(
            string ticker, int maxPerConcept, CancellationToken ct = default)
        {
            if (throwForTicker is not null && string.Equals(ticker, throwForTicker, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"EDGAR fetch failed for {ticker}");
            }

            return Task.FromResult(facts);
        }
    }

    private sealed class FakeMarketDataService : IMarketDataService
    {
        public Task<IReadOnlyDictionary<string, QuoteCacheEntry>> GetQuotesAsync(
            IReadOnlyCollection<string> tickers, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, QuoteCacheEntry>>(
                new Dictionary<string, QuoteCacheEntry>());

        public Task<IReadOnlyList<DailyClose>> GetDailyClosesAsync(
            string ticker, DateOnly since, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DailyClose>>([]);
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

        public Task GenerateLowBalanceAlertAsync(
            Guid userId, Guid accountId, string accountName, decimal balance, decimal threshold, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ResolveLowBalanceAlertAsync(Guid userId, Guid accountId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateSyncFailureAlertAsync(
            Guid userId, string provider, Guid? accountId, string? accountName, string? errorCode, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ResolveSyncFailureAlertAsync(Guid userId, string provider, Guid? accountId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateUnusualSpendAlertAsync(
            Guid userId, string category, decimal currentMonthSpend, decimal averageMonthlySpend, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteAlertsForAccountAsync(Guid accountId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateConsentExpiringAlertAsync(
            Guid userId, Guid referenceId, string providerName, DateTime expiresAt, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task GenerateJobFailureAlertAsync(
            Guid userId, Guid referenceId, string jobName, int consecutiveCount, string? lastError, CancellationToken ct = default)
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
            Guid userId, Guid referenceId, string ticker, string reason, CancellationToken ct = default)
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
    }
}
