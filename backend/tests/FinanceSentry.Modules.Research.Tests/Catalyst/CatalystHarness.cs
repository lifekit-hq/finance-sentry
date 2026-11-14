namespace FinanceSentry.Modules.Research.Tests.Catalyst;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Catalyst;
using FinanceSentry.Modules.Research.Application.Services.Coverage;
using FinanceSentry.Modules.Research.Application.Services.ReferenceUniverse;
using FinanceSentry.Modules.Research.Domain.Catalyst;
using FinanceSentry.Modules.Research.Domain.Coverage;
using FinanceSentry.Modules.Research.Domain.ReferenceUniverse;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.Research.Tests.Coverage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

/// <summary>
/// The catalyst detection over a real in-memory <see cref="CoverageHarness"/> store, the real repositories, the real reference
/// universe reader and the real filing scanner; only the outside world (the two feeds, the candidate scorer, the alert generator
/// and the coverage event reviewer) is scripted.
/// </summary>
internal sealed class CatalystHarness
{
    public static readonly Guid UserId = CoverageHarness.UserId;

    public static readonly DateTimeOffset Now = new(2026, 11, 16, 0, 30, 0, TimeSpan.Zero);

    public static readonly DateOnly Today = new(2026, 11, 16);

    public CatalystHarness(CatalystOptions? options = null)
    {
        Options = options ?? new CatalystOptions();
        Coverage = new CoverageHarness();
        Coverage.Ips.Setup(i => i.GetUserIdsWithCurrentIpsUnscopedAsync(It.IsAny<CancellationToken>())).ReturnsAsync([UserId]);
        Clock = new AutoAdvanceClock(Now);
        Alerts.Setup(a => a.GenerateReviewDueAlertAsync(It.IsAny<Guid>(), It.IsAny<ReviewDue>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        Reviewer.Setup(r => r.ReviewAsync(It.IsAny<Guid>(), It.IsAny<ReviewSubjects>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, ReviewSubjects, CancellationToken>((_, subjects, _) => Hits.AddRange(subjects.CoverageHits))
            .Returns(Task.CompletedTask);
        Scorer.Setup(s => s.Handle(It.IsAny<ScoreCandidateCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ScoreCandidateCommand, CancellationToken>((c, _) => Nominations.Add(c))
            .ReturnsAsync((ScoreCandidateCommand c, CancellationToken _) => new ScoreCandidateResult(Guid.NewGuid(), c.Ticker, true, null!));
    }

    public CatalystOptions Options { get; }

    public CoverageHarness Coverage { get; }

    public AutoAdvanceClock Clock { get; }

    public ScriptedFullText FullText { get; } = new();

    public Mock<ISecEdgarService> Edgar { get; } = new();

    public Mock<ICommandHandler<ScoreCandidateCommand, ScoreCandidateResult>> Scorer { get; } = new();

    public Mock<IAlertGeneratorService> Alerts { get; } = new();

    public Mock<ICoverageEventReviewer> Reviewer { get; } = new();

    public List<ScoreCandidateCommand> Nominations { get; } = [];

    public List<CoverageHit> Hits { get; } = [];

    public ReferenceUniverseReader Reference => new(
        new ReferenceUniverseRepository(Coverage.Db), Microsoft.Extensions.Options.Options.Create(new ReferenceUniverseOptions()), Clock);

    public CatalystFilingScanner Scanner => new(
        FullText, Edgar.Object, RequestPacer.PerSecond(Options.SecRequestsPerSecond, Clock),
        Microsoft.Extensions.Options.Options.Create(Options), NullLogger<CatalystFilingScanner>.Instance);

    public CatalystDetector Detector => new(
        Reference, Scanner, new CatalystRepository(Coverage.Db), Coverage.Coverage, Coverage.Ips.Object, Scorer.Object,
        new ReviewDueRaiser(new ThesisRepository(Coverage.Db), new CandidateRepository(Coverage.Db), Alerts.Object, Reviewer.Object),
        Microsoft.Extensions.Options.Options.Create(Options), Clock, NullLogger<CatalystDetector>.Instance);

    /// <summary>Puts tickers on a reference list.</summary>
    public async Task ListAsync(string listKey, params string[] tickers)
    {
        Coverage.Db.ReferenceListMembers.AddRange(tickers.Select(t => new ReferenceListMember
        {
            ListKey = listKey, Ticker = t, Source = listKey.StartsWith("theme:", StringComparison.Ordinal) ? ReferenceSources.Themes : ReferenceSources.Sp500,
        }));
        await Coverage.Db.SaveChangesAsync();
    }

    public void Filed(string item, params CatalystFiling[] filings)
    {
        if (!FullText.ByItem.TryGetValue(item, out var list))
        {
            FullText.ByItem[item] = list = [];
        }

        list.AddRange(filings);
    }

    public async Task<List<CatalystEvent>> EventsAsync()
        => await Coverage.Db.CatalystEvents.IgnoreQueryFilters().AsNoTracking().OrderBy(e => e.Ticker).ThenBy(e => e.Accession).ToListAsync();

    public async Task RemovedAsync(string ticker, DateTimeOffset at, CoverageReason reason = CoverageReason.Screen)
    {
        Coverage.Db.CoverageJournal.Add(new CoverageJournalEntry
        {
            UserId = UserId, Ticker = ticker, Action = CoverageAction.Removed, Rule = CoverageRuleIds.NoEvent, Reason = reason, Detail = "idle", At = at,
        });
        await Coverage.Db.SaveChangesAsync();
        Coverage.Db.ChangeTracker.Clear();
    }
}
