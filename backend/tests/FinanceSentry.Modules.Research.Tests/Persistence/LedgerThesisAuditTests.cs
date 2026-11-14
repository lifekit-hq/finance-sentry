namespace FinanceSentry.Modules.Research.Tests.Persistence;

using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// Ledger writes theses itself (the owner decides only buy or not), so every create and revision it makes must leave
/// the journal row that shows it and lets the owner undo it. These tests drive the production handlers over the real
/// <see cref="ThesisRepository"/> (SQLite) and the real <see cref="ThesisEventRecorder"/>, and assert on the rows that
/// would be persisted in <c>thesis_events</c>.
/// </summary>
public sealed class LedgerThesisAuditTests
{
    private const string Ticker = "NVDA";

    private static readonly Guid UserId = Guid.NewGuid();

    private static readonly ThesisInvalidationTrigger Drawdown30 = new("price_drawdown", "greaterThan", 0.30m);
    private static readonly ThesisInvalidationTrigger Drawdown45 = new("price_drawdown", "greaterThan", 0.45m);

    private sealed class Harness(ThesisSqliteFixture fixture)
    {
        public InMemoryThesisEventRepository Journal { get; } = new();

        public async Task<ThesisDto> SaveAsync(
            ThesisEventOrigin origin,
            string text,
            ThesisInvalidationTrigger trigger,
            Guid? id = null,
            string? rationale = null,
            ThesisEvidenceRefs? evidence = null)
        {
            await using var ctx = fixture.CreateContext(UserId);
            var handler = new SaveThesisCommandHandler(
                new ThesisRepository(ctx), Recorder(), new ResearchWriteScope(ctx), new ThesisOriginReader(Journal),
                NullLogger<SaveThesisCommandHandler>.Instance);
            return await handler.Handle(
                new SaveThesisCommand(
                    UserId, id, Ticker, text, [], [], [trigger],
                    Origin: origin, Rationale: rationale, Evidence: evidence),
                CancellationToken.None);
        }

        public async Task<RevertThesisRevisionResult> RevertAsync(Guid eventId, ThesisEventOrigin origin, string? rationale = null)
        {
            await using var ctx = fixture.CreateContext(UserId);
            var handler = new RevertThesisRevisionCommandHandler(new ThesisRepository(ctx), Journal, Recorder(), new ResearchWriteScope(ctx));
            return await handler.Handle(
                new RevertThesisRevisionCommand(UserId, eventId, rationale, origin), CancellationToken.None);
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
                .ReturnsAsync(new Dictionary<string, QuoteCacheEntry>());
            return new ThesisEventRecorder(Journal, quotes.Object, NullLogger<ThesisEventRecorder>.Instance);
        }
    }

    [Fact]
    public async Task Create_ByLedger_StampsLedgerOrigin_AndAuditsTheNewVersionWithRationaleAndEvidence()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var h = new Harness(fixture);
        var evidence = new ThesisEvidenceRefs(
            NewsArticleIds: ["n-1"], FilingAccessions: ["0001045810-26-000021"], EventIds: ["e-9"], FundamentalsAsOf: "2026-07-31");

        var saved = await h.SaveAsync(
            ThesisEventOrigin.Ledger, "Data-center demand outruns supply.", Drawdown30,
            rationale: "Q2 print beat on data-center revenue", evidence: evidence);

        saved.Origin.Should().Be("Ledger");
        var created = h.Journal.Rows.Should().ContainSingle().Subject;
        created.EventType.Should().Be(ThesisEventType.Created);
        created.SubjectType.Should().Be(ThesisSubjectType.Thesis);
        created.SubjectId.Should().Be(saved.Id);
        created.Origin.Should().Be(ThesisEventOrigin.Ledger);
        created.Audit!.Prior.Should().BeNull();
        created.Audit.New!.ThesisText.Should().Be("Data-center demand outruns supply.");
        created.Audit.New.InvalidationTriggers.Should().Equal(Drawdown30);
        created.Audit.Rationale.Should().Be("Q2 print beat on data-center revenue");
        created.Audit.Evidence.Should().Be(evidence);
    }

    [Fact]
    public async Task Create_ByOwner_StampsUserOrigin()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var h = new Harness(fixture);

        var saved = await h.SaveAsync(ThesisEventOrigin.User, "Owner thesis.", Drawdown30);

        saved.Origin.Should().Be("User");
        h.Journal.Rows.Single().Origin.Should().Be(ThesisEventOrigin.User);
    }

    [Fact]
    public async Task Revise_ByLedger_AppendsRevisedEvent_WithPriorAndNewTextAndTriggers()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var h = new Harness(fixture);
        var created = await h.SaveAsync(ThesisEventOrigin.User, "Original case.", Drawdown30);

        var revised = await h.SaveAsync(
            ThesisEventOrigin.Ledger, "Narrowed case after the print.", Drawdown45, id: created.Id,
            rationale: "Guidance cut", evidence: new ThesisEvidenceRefs(NewsArticleIds: ["n-2"]));

        revised.Origin.Should().Be("Ledger");
        var revision = h.Journal.Rows.Should().Contain(e => e.EventType == ThesisEventType.Revised).Subject;
        revision.Origin.Should().Be(ThesisEventOrigin.Ledger);
        revision.SubjectId.Should().Be(created.Id);
        revision.PricesPending.Should().BeFalse("an edit is not a price point");
        revision.SubjectPrice.Should().BeNull();
        revision.Audit!.Prior!.ThesisText.Should().Be("Original case.");
        revision.Audit.Prior.InvalidationTriggers.Should().Equal(Drawdown30);
        revision.Audit.New!.ThesisText.Should().Be("Narrowed case after the print.");
        revision.Audit.New.InvalidationTriggers.Should().Equal(Drawdown45);
        revision.Audit.Rationale.Should().Be("Guidance cut");
        revision.Audit.Evidence!.NewsArticleIds.Should().Equal("n-2");
    }

    [Fact]
    public async Task Revise_WithIdenticalContent_AppendsNoEvent_AndKeepsTheStandingOrigin()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var h = new Harness(fixture);
        var created = await h.SaveAsync(ThesisEventOrigin.User, "Same case.", Drawdown30);

        var again = await h.SaveAsync(ThesisEventOrigin.Ledger, "Same case.", Drawdown30, id: created.Id);

        h.Journal.Rows.Should().ContainSingle(e => e.EventType == ThesisEventType.Created);
        again.Origin.Should().Be("User", "nothing changed, so Ledger made no change to attribute");
    }

    [Fact]
    public async Task Origin_ReadsTheLatestWriter()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var h = new Harness(fixture);
        var created = await h.SaveAsync(ThesisEventOrigin.Ledger, "Ledger case.", Drawdown30);
        var reader = new ThesisOriginReader(h.Journal);

        (await reader.ForThesesAsync(UserId, [created.Id], CancellationToken.None))[created.Id]
            .Should().Be(ThesisEventOrigin.Ledger);

        await h.SaveAsync(ThesisEventOrigin.User, "Owner edit.", Drawdown30, id: created.Id);

        (await reader.ForThesesAsync(UserId, [created.Id], CancellationToken.None))[created.Id]
            .Should().Be(ThesisEventOrigin.User);
        ThesisOriginReader.Resolve(new Dictionary<Guid, ThesisEventOrigin>(), Guid.NewGuid())
            .Should().Be(ThesisEventOrigin.User, "an entry written before the audit existed reads as User");
    }

    [Fact]
    public async Task Revert_RestoresThePriorVersion_AndJournalsTheReversal()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var h = new Harness(fixture);
        var created = await h.SaveAsync(ThesisEventOrigin.User, "Original case.", Drawdown30);
        await h.SaveAsync(ThesisEventOrigin.Ledger, "Ledger rewrite.", Drawdown45, id: created.Id);
        var revision = h.Journal.Rows.Single(e => e.EventType == ThesisEventType.Revised);

        var result = await h.RevertAsync(revision.Id, ThesisEventOrigin.User, "Ledger over-reached");

        result.Outcome.Should().Be(RevertOutcome.Reverted);
        result.Thesis!.ThesisText.Should().Be("Original case.");
        var stored = await h.ReadAsync(created.Id);
        stored.ThesisText.Should().Be("Original case.");
        stored.InvalidationTriggers.Should().Equal(Drawdown30);
        var reversal = h.Journal.Rows.Should().Contain(e => e.EventType == ThesisEventType.Reverted).Subject;
        reversal.Origin.Should().Be(ThesisEventOrigin.User);
        reversal.Audit!.RevertsEventId.Should().Be(revision.Id);
        reversal.Audit.Prior!.ThesisText.Should().Be("Ledger rewrite.");
        reversal.Audit.New!.ThesisText.Should().Be("Original case.");
        reversal.Audit.Rationale.Should().Be("Ledger over-reached");
        reversal.PricesPending.Should().BeFalse();
    }

    [Fact]
    public async Task Revert_ByLedger_IsStampedLedger_AndTheReversalCanItselfBeUndone()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var h = new Harness(fixture);
        var created = await h.SaveAsync(ThesisEventOrigin.User, "Original case.", Drawdown30);
        await h.SaveAsync(ThesisEventOrigin.Ledger, "Ledger rewrite.", Drawdown45, id: created.Id);
        var revision = h.Journal.Rows.Single(e => e.EventType == ThesisEventType.Revised);

        (await h.RevertAsync(revision.Id, ThesisEventOrigin.Ledger)).Outcome.Should().Be(RevertOutcome.Reverted);
        var reversal = h.Journal.Rows.Single(e => e.EventType == ThesisEventType.Reverted);
        reversal.Origin.Should().Be(ThesisEventOrigin.Ledger);

        (await h.RevertAsync(reversal.Id, ThesisEventOrigin.User)).Outcome.Should().Be(RevertOutcome.Reverted);
        (await h.ReadAsync(created.Id)).ThesisText.Should().Be("Ledger rewrite.");
    }

    [Fact]
    public async Task Revert_AfterALaterChange_IsRefused_AndLeavesTheThesisAlone()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var h = new Harness(fixture);
        var created = await h.SaveAsync(ThesisEventOrigin.User, "Original case.", Drawdown30);
        await h.SaveAsync(ThesisEventOrigin.Ledger, "First rewrite.", Drawdown45, id: created.Id);
        var firstRevision = h.Journal.Rows.Single(e => e.EventType == ThesisEventType.Revised);
        await h.SaveAsync(ThesisEventOrigin.User, "Owner's later edit.", Drawdown45, id: created.Id);

        var result = await h.RevertAsync(firstRevision.Id, ThesisEventOrigin.User);

        result.Outcome.Should().Be(RevertOutcome.ThesisChangedSince);
        (await h.ReadAsync(created.Id)).ThesisText.Should().Be("Owner's later edit.");
        h.Journal.Rows.Should().NotContain(e => e.EventType == ThesisEventType.Reverted);
    }

    [Fact]
    public async Task Revert_OfAnEventThatIsNotARevision_OrAnUnknownEvent_IsRefused()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var h = new Harness(fixture);
        await h.SaveAsync(ThesisEventOrigin.Ledger, "Ledger case.", Drawdown30);
        var createdEvent = h.Journal.Rows.Single();

        (await h.RevertAsync(createdEvent.Id, ThesisEventOrigin.User)).Outcome.Should().Be(RevertOutcome.NotARevision);
        (await h.RevertAsync(Guid.NewGuid(), ThesisEventOrigin.User)).Outcome.Should().Be(RevertOutcome.EventNotFound);
    }

    [Fact]
    public async Task Revert_ForAnotherUsersEvent_IsNotFound()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var h = new Harness(fixture);
        var created = await h.SaveAsync(ThesisEventOrigin.User, "Original case.", Drawdown30);
        await h.SaveAsync(ThesisEventOrigin.Ledger, "Ledger rewrite.", Drawdown45, id: created.Id);
        var revision = h.Journal.Rows.Single(e => e.EventType == ThesisEventType.Revised);
        revision.UserId = Guid.NewGuid();

        (await h.RevertAsync(revision.Id, ThesisEventOrigin.User)).Outcome.Should().Be(RevertOutcome.EventNotFound);
    }

    private static async Task<ThesisDto> SaveWithJournalDownAsync(
        ThesisSqliteFixture fixture, ThesisEventOrigin origin, Guid? id, string text)
    {
        await using var ctx = fixture.CreateContext(UserId);
        var handler = new SaveThesisCommandHandler(
            new ThesisRepository(ctx),
            new RecordingThesisEventRecorder { FailWith = new InvalidOperationException("journal down") },
            new ResearchWriteScope(ctx),
            new ThesisOriginReader(new InMemoryThesisEventRepository()),
            NullLogger<SaveThesisCommandHandler>.Instance);
        return await handler.Handle(
            new SaveThesisCommand(UserId, id, Ticker, text, [], [], [Drawdown45], Origin: origin),
            CancellationToken.None);
    }

    [Fact]
    public async Task Revise_ByLedger_WhenTheJournalIsDown_FailsAndRollsTheRevisionBack_ButTheOwnersEditSurvives()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var h = new Harness(fixture);
        var created = await h.SaveAsync(ThesisEventOrigin.User, "Original case.", Drawdown30);

        await ((Func<Task>)(() => SaveWithJournalDownAsync(fixture, ThesisEventOrigin.Ledger, created.Id, "Edit by Ledger.")))
            .Should().ThrowAsync<InvalidOperationException>();

        var afterLedgerFailure = await h.ReadAsync(created.Id);
        afterLedgerFailure.ThesisText.Should().Be("Original case.", "an unjournaled Ledger change must not persist");
        afterLedgerFailure.InvalidationTriggers.Should().Equal(Drawdown30);

        (await SaveWithJournalDownAsync(fixture, ThesisEventOrigin.User, created.Id, "Edit by User."))
            .ThesisText.Should().Be("Edit by User.");
    }

    [Fact]
    public async Task Create_ByLedger_WhenTheJournalIsDown_FailsAndLeavesNoThesis_SoARetryCannotDuplicateIt()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();

        await ((Func<Task>)(() => SaveWithJournalDownAsync(fixture, ThesisEventOrigin.Ledger, id: null, "Ledger thesis.")))
            .Should().ThrowAsync<InvalidOperationException>();

        await using var readCtx = fixture.CreateContext(UserId);
        (await new ThesisRepository(readCtx).FindByTickerAsync(UserId, Ticker, CancellationToken.None))
            .Should().BeEmpty();
    }
}
