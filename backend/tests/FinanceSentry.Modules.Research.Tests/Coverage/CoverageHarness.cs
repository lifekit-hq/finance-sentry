namespace FinanceSentry.Modules.Research.Tests.Coverage;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Coverage;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Coverage;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

/// <summary>A verdict source the test scripts per ticker; null means "the scorecard could not be read".</summary>
internal sealed class ScriptedVerdictSource : ICoverageVerdictSource
{
    public Dictionary<string, string?> Verdicts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Reads { get; } = [];

    public Task<CoverageVerdictReading?> ReadAsync(string ticker, CancellationToken ct = default)
    {
        Reads.Add(ticker);
        return Task.FromResult(Verdicts.TryGetValue(ticker, out var word) && word is not null
            ? new CoverageVerdictReading(word, "scripted")
            : null);
    }
}

/// <summary>A correlation reader the test scripts per ticker; an unlisted ticker reads "not evaluable".</summary>
internal sealed class ScriptedCorrelationReader : IReturnCorrelationReader
{
    public Dictionary<string, decimal> Book { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<TickerCorrelation> GetAsync(
        string ticker, IReadOnlyCollection<CorrelationReference> book, int topHoldings, decimal minBookCoverage,
        CancellationToken ct = default)
        => Task.FromResult(new TickerCorrelation(
            ticker, 63, [],
            Book.TryGetValue(ticker, out var value)
                ? new BookCorrelation(value, 63, 1m, null)
                : new BookCorrelation(null, 0, 0m, CorrelationReasons.NoBars)));
}

/// <summary>
/// The coverage evaluation over a real in-memory <see cref="ResearchDbContext"/> and the real repositories; only the
/// outside world (holdings, watchlist, the scorecard verdict, correlation, the earnings calendar) is scripted.
/// </summary>
internal sealed class CoverageHarness
{
    public static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static readonly DateTimeOffset Day1 = new(2026, 11, 14, 1, 10, 0, TimeSpan.Zero);

    public CoverageHarness(CoverageOptions? options = null)
    {
        Options = options ?? new CoverageOptions();
        Db = new ResearchDbContext(
            new DbContextOptionsBuilder<ResearchDbContext>().UseInMemoryDatabase($"coverage-{Guid.NewGuid():N}").Options,
            new FixedCurrentUser(null));

        Brokerage.Setup(b => b.GetHoldingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Holdings);
        Watchlist.Setup(w => w.ListTickersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => WatchedTickers);
        Banking.Setup(b => b.GetActiveUserIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => Users);
        Ips.Setup(i => i.GetUserIdsWithCurrentIpsUnscopedAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        Earnings
            .Setup(e => e.GetForTickersAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync((IReadOnlyCollection<string> tickers, DateOnly _, DateOnly _, string? _, CancellationToken _, bool _) =>
                [.. EarningsDates.Where(e => tickers.Contains(e.Key, StringComparer.OrdinalIgnoreCase))
                    .Select(e => new EarningsEvent(e.Key, EarningsEventType.Earnings, e.Value, false, "test"))]);
    }

    public CoverageOptions Options { get; }

    public ResearchDbContext Db { get; }

    public List<BrokerageHoldingSummary> Holdings { get; } = [];

    public List<string> WatchedTickers { get; } = [];

    public List<Guid> Users { get; } = [UserId];

    public Dictionary<string, DateOnly> EarningsDates { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ScriptedVerdictSource Verdicts { get; } = new();

    public ScriptedCorrelationReader Correlation { get; } = new();

    public Mock<IBrokerageHoldingsReader> Brokerage { get; } = new();

    public Mock<IWatchlistReader> Watchlist { get; } = new();

    public Mock<IBankingTotalsReader> Banking { get; } = new();

    public Mock<IIpsRepository> Ips { get; } = new();

    public Mock<IEarningsCalendarService> Earnings { get; } = new();

    public OpportunityOptions Opportunity { get; } = new();

    public CoverageRepository Coverage => new(Db);

    public CoverageSnapshotReader Snapshots => new(
        Brokerage.Object, Watchlist.Object, new CandidateRepository(Db), new CandidateScoreRepository(Db),
        new ThesisRepository(Db), new ThesisEventRepository(Db), Coverage, Microsoft.Extensions.Options.Options.Create(Options));

    public CoverageEvaluator Evaluator => new(
        Snapshots, Coverage, Verdicts, Correlation, Earnings.Object, Banking.Object, Ips.Object,
        Microsoft.Extensions.Options.Options.Create(Options), Microsoft.Extensions.Options.Options.Create(Opportunity),
        NullLogger<CoverageEvaluator>.Instance);

    public CoverageExplainer Explainer => new(
        Snapshots, Coverage, Correlation, Earnings.Object,
        Microsoft.Extensions.Options.Options.Create(Options), Microsoft.Extensions.Options.Options.Create(Opportunity),
        NullLogger<CoverageExplainer>.Instance);

    public Task<CoverageRunSummary> RunAsync(DateTimeOffset asOf) => Evaluator.EvaluateUserAsync(UserId, asOf);

    public void Hold(string symbol, decimal usd = 1000m, string type = "STK")
        => Holdings.Add(new BrokerageHoldingSummary(symbol, type, 1m, usd, DateTime.UtcNow, "ibkr"));

    /// <summary>An Active candidate, optionally scored, optionally with the creation event that carries a decision note.</summary>
    public async Task<OpportunityCandidate> CandidateAsync(
        string ticker,
        CandidateSource source = CandidateSource.Scan,
        CandidateStatus status = CandidateStatus.Active,
        int? structure = 70,
        int? fundamentals = 70,
        DateTimeOffset? scoredAt = null,
        string? decisionNote = null,
        DateTimeOffset? createdAt = null)
    {
        var created = createdAt ?? Day1.AddDays(-3);
        var candidate = new OpportunityCandidate
        {
            UserId = UserId,
            Ticker = ticker,
            Source = source,
            Status = status,
            CreatedAt = created,
            ExpiresAt = created.AddDays(30),
        };
        Db.OpportunityCandidates.Add(candidate);
        if (structure is not null || fundamentals is not null)
        {
            Db.CandidateScores.Add(new CandidateScore
            {
                CandidateId = candidate.Id,
                ScoredAt = scoredAt ?? Day1.AddDays(-1),
                StructureScore = structure,
                FundamentalsScore = fundamentals,
            });
        }

        if (decisionNote is not null)
        {
            Db.ThesisEvents.Add(new ThesisEvent
            {
                UserId = UserId,
                SubjectType = ThesisSubjectType.Candidate,
                SubjectId = candidate.Id,
                Ticker = ticker,
                EventType = ThesisEventType.Created,
                Timestamp = created,
                DecisionNote = decisionNote,
            });
        }

        await Db.SaveChangesAsync();
        return candidate;
    }

    public async Task<InvestmentThesis> ThesisAsync(string ticker, DateOnly? catalyst = null, bool broken = false)
    {
        var thesis = new InvestmentThesis
        {
            UserId = UserId,
            Ticker = ticker,
            ThesisText = "t",
            BrokenAt = broken ? Day1 : null,
            Catalysts = catalyst is { } date ? [new ThesisCatalyst(date, "launch")] : [],
        };
        Db.Theses.Add(thesis);
        await Db.SaveChangesAsync();
        return thesis;
    }

    public async Task<CoverageMember> MemberAsync(
        string ticker, CoverageReason reason = CoverageReason.Screen, DateTimeOffset? addedAt = null, DateTimeOffset? lastEvent = null)
    {
        var member = new CoverageMember
        {
            UserId = UserId,
            Ticker = ticker,
            AddedReason = reason,
            AddedAt = addedAt ?? Day1.AddDays(-10),
            LastEventAt = lastEvent,
        };
        Db.CoverageMembers.Add(member);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return member;
    }

    public Task<List<CoverageMember>> MembersAsync()
        => Db.CoverageMembers.IgnoreQueryFilters().AsNoTracking().OrderBy(m => m.Ticker).ToListAsync();

    public Task<List<CoverageJournalEntry>> JournalAsync()
        => Db.CoverageJournal.IgnoreQueryFilters().AsNoTracking().OrderBy(e => e.At).ThenBy(e => e.Ticker).ToListAsync();

    public async Task<string[]> TickersAsync() => [.. (await MembersAsync()).Select(m => m.Ticker)];
}
