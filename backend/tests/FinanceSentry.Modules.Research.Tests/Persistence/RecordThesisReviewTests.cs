namespace FinanceSentry.Modules.Research.Tests.Persistence;

using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// A thesis review that ends in "no change" is journaled on the thesis itself (#1518): it needs no companion event,
/// leaves the thesis untouched and reads back through the thesis's event trail with its verdict and reasoning.
/// </summary>
public sealed class RecordThesisReviewTests
{
    private const string Ticker = "MU";

    private static readonly Guid UserId = Guid.NewGuid();

    private sealed class Harness(ThesisSqliteFixture fixture)
    {
        public InMemoryThesisEventRepository Journal { get; } = new();

        public async Task<Guid> SeedThesisAsync(Guid userId)
        {
            await using var ctx = fixture.CreateContext(userId);
            var thesis = new InvestmentThesis
            {
                UserId = userId,
                Ticker = Ticker,
                ThesisText = "DRAM upcycle.",
                InvalidationTriggers = [new ThesisInvalidationTrigger("price_drawdown", "greaterThan", 0.30m)],
            };
            await new ThesisRepository(ctx).UpsertAsync(thesis, CancellationToken.None);
            return thesis.Id;
        }

        public async Task<RecordThesisReviewResult> ReviewAsync(
            Guid thesisId,
            string verdict,
            string rationale,
            ThesisEventOrigin origin = ThesisEventOrigin.Ledger,
            ThesisEvidenceRefs? evidence = null)
        {
            await using var ctx = fixture.CreateContext(UserId);
            var handler = new RecordThesisReviewCommandHandler(
                new ThesisRepository(ctx), Recorder(), new ResearchWriteScope(ctx));
            return await handler.Handle(
                new RecordThesisReviewCommand(UserId, thesisId, verdict, rationale, evidence, origin),
                CancellationToken.None);
        }

        public async Task<InvestmentThesis> ReadAsync(Guid id)
        {
            await using var ctx = fixture.CreateContext(UserId);
            return (await new ThesisRepository(ctx).FindAsync(UserId, id, CancellationToken.None))!;
        }

        private ThesisEventRecorder Recorder()
        {
            var quotes = new Mock<IMarketDataService>();
            quotes.Setup(q => q.GetQuotesAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<string, QuoteCacheEntry>
                {
                    [Ticker] = new() { Ticker = Ticker, Price = 88.5m },
                    [ThesisEventRecorder.DefaultBenchmarkTicker] = new()
                    {
                        Ticker = ThesisEventRecorder.DefaultBenchmarkTicker,
                        Price = 600m,
                    },
                });
            return new ThesisEventRecorder(Journal, quotes.Object, NullLogger<ThesisEventRecorder>.Instance);
        }
    }

    [Fact]
    public async Task NoChangeReview_IsJournaledOnTheThesis_AndReadsBackThroughItsEventTrail()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var h = new Harness(fixture);
        var thesisId = await h.SeedThesisAsync(UserId);
        var before = await h.ReadAsync(thesisId);
        var evidence = new ThesisEvidenceRefs(NewsArticleIds: ["n-1"], FundamentalsAsOf: "2026-11-20");

        var result = await h.ReviewAsync(
            thesisId, "  no change ", " Drawdown is sector-wide; contract pricing intact. ", evidence: evidence);

        result.ThesisId.Should().Be(thesisId);
        result.EventType.Should().Be(ThesisEventType.Reviewed);
        result.Verdict.Should().Be("no change");
        result.Origin.Should().Be("Ledger");

        var trail = await new ListThesisEventsQueryHandler(h.Journal)
            .Handle(new ListThesisEventsQuery(UserId, thesisId), CancellationToken.None);
        var review = trail.Should().ContainSingle().Subject;
        review.EventType.Should().Be(ThesisEventType.Reviewed);
        review.SubjectType.Should().Be(ThesisSubjectType.Thesis);
        review.Ticker.Should().Be(Ticker);
        review.DecisionNote.Should().Be("no change");
        review.Origin.Should().Be("Ledger");
        review.Audit!.Rationale.Should().Be("Drawdown is sector-wide; contract pricing intact.");
        review.Audit.Evidence.Should().Be(evidence);
        review.Audit.Prior.Should().BeNull();
        review.Audit.New.Should().BeNull();
        review.SubjectPrice.Should().Be(88.5m, "a review is a decision point, price-stamped so it can be graded");
        review.BenchmarkPrice.Should().Be(600m);

        var after = await h.ReadAsync(thesisId);
        ThesisVersion.Of(after).SameContentAs(ThesisVersion.Of(before)).Should().BeTrue("a review changes nothing in the thesis");
    }

    [Fact]
    public async Task OwnerReview_IsStampedWithUserOrigin()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var h = new Harness(fixture);
        var thesisId = await h.SeedThesisAsync(UserId);

        await h.ReviewAsync(thesisId, "hold", "Nothing material.", ThesisEventOrigin.User);

        h.Journal.Rows.Single().Origin.Should().Be(ThesisEventOrigin.User);
    }

    [Theory]
    [InlineData("", "reason")]
    [InlineData("   ", "reason")]
    [InlineData("no change", "")]
    [InlineData("no change", "  ")]
    public async Task BlankVerdictOrRationale_IsRefused_AndJournalsNothing(string verdict, string rationale)
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var h = new Harness(fixture);
        var thesisId = await h.SeedThesisAsync(UserId);

        var act = () => h.ReviewAsync(thesisId, verdict, rationale);

        await act.Should().ThrowAsync<InvalidThesisReviewException>();
        h.Journal.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task OverLongVerdictOrRationale_IsRefused()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var h = new Harness(fixture);
        var thesisId = await h.SeedThesisAsync(UserId);

        await FluentActions.Awaiting(() => h.ReviewAsync(thesisId, new string('v', 201), "reason"))
            .Should().ThrowAsync<InvalidThesisReviewException>();
        await FluentActions.Awaiting(() => h.ReviewAsync(thesisId, "no change", new string('r', 2001)))
            .Should().ThrowAsync<InvalidThesisReviewException>();
        h.Journal.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task UnknownOrForeignThesis_IsNotFound_AndJournalsNothing()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var h = new Harness(fixture);
        var foreign = await h.SeedThesisAsync(Guid.NewGuid());

        await FluentActions.Awaiting(() => h.ReviewAsync(Guid.NewGuid(), "no change", "reason"))
            .Should().ThrowAsync<ThesisNotFoundException>();
        await FluentActions.Awaiting(() => h.ReviewAsync(foreign, "no change", "reason"))
            .Should().ThrowAsync<ThesisNotFoundException>();
        h.Journal.Rows.Should().BeEmpty();
    }
}
