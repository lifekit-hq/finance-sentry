namespace FinanceSentry.Modules.Research.Tests.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Coverage;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Moq;

/// <summary>
/// The collaborators every "detector reads coverage names" test shares: one user, an empty book, a scripted coverage list
/// and a reviewer that records the hits the detector handed it instead of acting on them.
/// </summary>
public abstract class CoverageDetectorFixture
{
    protected readonly Guid UserId = Guid.NewGuid();
    protected readonly Mock<IBankingTotalsReader> Banking = new();
    protected readonly Mock<IBrokerageHoldingsReader> Brokerage = new();
    protected readonly Mock<IWatchlistReader> Watchlist = new();
    protected readonly Mock<IThesisRepository> Theses = new();
    protected readonly Mock<ICandidateRepository> Candidates = new();
    protected readonly Mock<ICryptoHoldingsReader> Crypto = new();
    protected readonly Mock<IAlertGeneratorService> Alerts = new();
    protected readonly Mock<ICoverageEventReviewer> Reviewer = new();
    protected readonly List<CoverageHit> Hits = [];
    protected readonly List<ReviewDue> Reviews = [];
    protected int ReviewerCalls;

    protected CoverageDetectorFixture()
    {
        Banking.Setup(b => b.GetActiveUserIdsAsync(default)).ReturnsAsync([UserId]);
        Brokerage.Setup(b => b.GetHoldingsAsync(UserId, default)).ReturnsAsync([]);
        Watchlist.Setup(w => w.ListTickersAsync(UserId, default)).ReturnsAsync([]);
        Theses.Setup(t => t.ListUnscopedAsync(UserId, default)).ReturnsAsync([]);
        Candidates.Setup(c => c.ListAllUnscopedAsync(UserId, default)).ReturnsAsync([]);
        Crypto.Setup(c => c.GetHoldingsAsync(UserId, default)).ReturnsAsync([]);
        Alerts.Setup(a => a.GenerateReviewDueAlertAsync(UserId, It.IsAny<ReviewDue>(), default))
            .Callback<Guid, ReviewDue, CancellationToken>((_, review, _) => Reviews.Add(review))
            .Returns(Task.CompletedTask);
        Reviewer.Setup(r => r.ReviewAsync(UserId, It.IsAny<ReviewSubjects>(), default))
            .Callback<Guid, ReviewSubjects, CancellationToken>((_, subjects, _) =>
            {
                ReviewerCalls++;
                Hits.AddRange(subjects.CoverageHits);
            })
            .Returns(Task.CompletedTask);
    }

    protected LookaheadUniverse Universe(params string[] coverage)
        => new(Brokerage.Object, Watchlist.Object, Theses.Object, Crypto.Object, CoverageStubs.WithMembers(coverage));

    protected ReviewDueRaiser Raiser() => new(Theses.Object, Candidates.Object, Alerts.Object, Reviewer.Object);

    protected void Hold(params string[] symbols)
        => Brokerage.Setup(b => b.GetHoldingsAsync(UserId, default))
            .ReturnsAsync([.. symbols.Select(s => new BrokerageHoldingSummary(s, "STK", 1m, 1000m, DateTime.UtcNow, "ibkr"))]);
}
