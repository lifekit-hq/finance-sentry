namespace FinanceSentry.Modules.Research.Tests.ThesisRevisions;

using FinanceSentry.Core.Auditing;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Domain.Revisions;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// The authorization boundary of propose-then-confirm: the confirm is the single step that writes a thesis, and only
/// the owner's own session may take it. Every negative case asserts that no write handler ran.
/// </summary>
public sealed class DecideThesisRevisionCommandTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly ThesisInvalidationTrigger Drawdown = new("price_drawdown", "greaterThan", 0.30m);
    private static readonly RiskGateVerdict Allowed = new(RiskGateDecision.Allowed, null, null, null, null);

    private readonly InMemoryThesisRevisionRepository _revisions = new();
    private readonly Mock<IThesisRepository> _theses = new();
    private readonly Mock<ICommandHandler<PromoteCandidateCommand, PromoteCandidateResult>> _promote = new();
    private readonly Mock<ICommandHandler<SaveThesisCommand, ThesisDto>> _save = new();
    private readonly Mock<ICommandHandler<DeleteThesisCommand, bool>> _delete = new();
    private readonly Mock<IThesisEventRecorder> _recorder = new();

    private DecideThesisRevisionCommandHandler Handler(AuditActorKind actor = AuditActorKind.OwnerSession)
    {
        var audit = new Mock<IAuditActor>();
        audit.SetupGet(a => a.Kind).Returns(actor);
        return new DecideThesisRevisionCommandHandler(
            _revisions, _theses.Object, _promote.Object, _save.Object, _delete.Object, _recorder.Object, audit.Object);
    }

    private ThesisRevision Pending(
        ThesisRevisionKind kind, Guid? subjectId = null, ThesisRevisionContent? content = null, DateTimeOffset? expiresAt = null)
    {
        var revision = new ThesisRevision
        {
            UserId = User,
            Kind = kind,
            ProposedBy = ThesisEventOrigin.Ledger,
            SubjectType = kind == ThesisRevisionKind.Promote ? ThesisSubjectType.Candidate : ThesisSubjectType.Thesis,
            SubjectId = subjectId ?? Guid.NewGuid(),
            Ticker = "CBRS",
            Rationale = "Filing changed the case.",
            Proposed = content ?? new ThesisRevisionContent(),
            Evidence = new ThesisEvidenceRefs(FilingAccessions: ["0001-26-1"]),
            ProposedUsd = 500m,
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddDays(10),
        };
        _revisions.Rows.Add(revision);
        return revision;
    }

    private InvestmentThesis Thesis(Guid id)
    {
        var thesis = new InvestmentThesis { Id = id, UserId = User, Ticker = "CBRS", ThesisText = "Original case.", InvalidationTriggers = [Drawdown] };
        _theses.Setup(r => r.FindAsync(User, id, It.IsAny<CancellationToken>())).ReturnsAsync(thesis);
        return thesis;
    }

    private static DecideThesisRevisionCommand Confirm(ThesisRevision r, bool overrideRisk = false)
        => new(User, r.Id, ThesisRevisionVerdict.Confirm, "ok", overrideRisk);

    private void NothingWasWritten()
    {
        _promote.Verify(h => h.Handle(It.IsAny<PromoteCandidateCommand>(), It.IsAny<CancellationToken>()), Times.Never);
        _save.Verify(h => h.Handle(It.IsAny<SaveThesisCommand>(), It.IsAny<CancellationToken>()), Times.Never);
        _delete.Verify(h => h.Handle(It.IsAny<DeleteThesisCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(AuditActorKind.McpService)]
    [InlineData(AuditActorKind.InAppAgent)]
    [InlineData(AuditActorKind.FleetAgent)]
    [InlineData(AuditActorKind.Job)]
    public async Task Confirm_ByAnyoneButTheOwner_IsRefused_AndWritesNothing(AuditActorKind actor)
    {
        var promotion = Pending(ThesisRevisionKind.Promote);
        var update = Pending(ThesisRevisionKind.NarrativeUpdate, content: new ThesisRevisionContent(ThesisText: "New."));
        var close = Pending(ThesisRevisionKind.Close);

        foreach (var revision in new[] { promotion, update, close })
        {
            var act = () => Handler(actor).Handle(Confirm(revision), CancellationToken.None);
            await act.Should().ThrowAsync<ThesisRevisionOwnerOnlyException>();
            revision.Status.Should().Be(ThesisRevisionStatus.Pending);
        }

        NothingWasWritten();
    }

    [Theory]
    [InlineData(AuditActorKind.McpService)]
    [InlineData(AuditActorKind.InAppAgent)]
    public async Task Reject_ByAnyoneButTheOwner_IsRefused(AuditActorKind actor)
    {
        var revision = Pending(ThesisRevisionKind.Close);

        var act = () => Handler(actor).Handle(
            new DecideThesisRevisionCommand(User, revision.Id, ThesisRevisionVerdict.Reject, "no"), CancellationToken.None);

        await act.Should().ThrowAsync<ThesisRevisionOwnerOnlyException>();
        revision.Status.Should().Be(ThesisRevisionStatus.Pending);
    }

    [Theory]
    [InlineData(AuditActorKind.OwnerSession)]
    [InlineData(AuditActorKind.McpPersonal)]
    public async Task ConfirmPromote_ByTheOwner_RunsThePromotionAsTheOwnersWrite(AuditActorKind actor)
    {
        var content = new ThesisRevisionContent(ThesisText: "Ledger's case.", InvalidationTriggers: [Drawdown]);
        var revision = Pending(ThesisRevisionKind.Promote, content: content);
        var thesisId = Guid.NewGuid();
        Thesis(thesisId);
        PromoteCandidateCommand? sent = null;
        _promote.Setup(h => h.Handle(It.IsAny<PromoteCandidateCommand>(), It.IsAny<CancellationToken>()))
            .Callback<PromoteCandidateCommand, CancellationToken>((c, _) => sent = c)
            .ReturnsAsync(new PromoteCandidateResult(thesisId, Allowed, CandidateFound: true));

        var result = await Handler(actor).Handle(Confirm(revision), CancellationToken.None);

        result.Outcome.Should().Be(ThesisRevisionOutcome.Confirmed);
        sent!.Origin.Should().Be(ThesisEventOrigin.User);
        sent.CandidateId.Should().Be(revision.SubjectId);
        sent.Content.Should().Be(content);
        sent.ProposedUsd.Should().Be(500m);
        sent.DecisionNote.Should().Be("Filing changed the case.");
        sent.Evidence.Should().Be(revision.Evidence);
        revision.Status.Should().Be(ThesisRevisionStatus.Confirmed);
        revision.ResultThesisId.Should().Be(thesisId);
        revision.DecidedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ConfirmPromote_TheRiskGateRefuses_LeavesTheProposalPending_AndOverrideIsTheOwnersExplicitCall()
    {
        var revision = Pending(ThesisRevisionKind.Promote);
        var refused = new RiskGateVerdict(RiskGateDecision.Refused, "MaxPositionWeight", 0.4m, 0.3m, 200m);
        _promote.Setup(h => h.Handle(It.IsAny<PromoteCandidateCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PromoteCandidateResult(null, refused, CandidateFound: true));

        var result = await Handler().Handle(Confirm(revision), CancellationToken.None);

        result.Outcome.Should().Be(ThesisRevisionOutcome.RiskGateRefused);
        result.Gate!.RuleKey.Should().Be("MaxPositionWeight");
        revision.Status.Should().Be(ThesisRevisionStatus.Pending);
        _promote.Verify(h => h.Handle(It.Is<PromoteCandidateCommand>(c => !c.OverrideRisk), It.IsAny<CancellationToken>()), Times.Once);

        await Handler().Handle(Confirm(revision, overrideRisk: true), CancellationToken.None);

        _promote.Verify(h => h.Handle(It.Is<PromoteCandidateCommand>(c => c.OverrideRisk), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmPromote_OfACandidateThatIsGone_IsAnErrorNotARiskRefusal()
    {
        var revision = Pending(ThesisRevisionKind.Promote);
        _promote.Setup(h => h.Handle(It.IsAny<PromoteCandidateCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PromoteCandidateResult(null, Allowed, CandidateFound: true));

        var act = () => Handler().Handle(Confirm(revision), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidThesisRevisionException>();
        revision.Status.Should().Be(ThesisRevisionStatus.Pending);
    }

    [Fact]
    public async Task ConfirmNarrativeUpdate_SavesTheMergedThesisAsTheOwner_WithRationaleAndEvidence()
    {
        var thesis = Thesis(Guid.NewGuid());
        var revision = Pending(
            ThesisRevisionKind.NarrativeUpdate, thesis.Id,
            new ThesisRevisionContent(ThesisText: "Narrowed case.", NarrativeFalsifiers: ["Loses the anchor customer"]));
        SaveThesisCommand? sent = null;
        _save.Setup(h => h.Handle(It.IsAny<SaveThesisCommand>(), It.IsAny<CancellationToken>()))
            .Callback<SaveThesisCommand, CancellationToken>((c, _) => sent = c)
            .ReturnsAsync(new ThesisDto(thesis.Id, "CBRS", "Narrowed case.", [], [], [Drawdown], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null));

        var result = await Handler().Handle(Confirm(revision), CancellationToken.None);

        result.Outcome.Should().Be(ThesisRevisionOutcome.Confirmed);
        sent!.Origin.Should().Be(ThesisEventOrigin.User);
        sent.Id.Should().Be(thesis.Id);
        sent.ThesisText.Should().Be("Narrowed case.");
        sent.InvalidationTriggers.Should().Equal([Drawdown], "a field the proposal leaves out stays as it is");
        sent.NarrativeFalsifiers.Should().Equal("Loses the anchor customer");
        sent.Rationale.Should().Contain("Ledger").And.Contain(revision.Id.ToString()).And.Contain("Filing changed the case.");
        sent.Evidence.Should().Be(revision.Evidence);
        revision.Status.Should().Be(ThesisRevisionStatus.Confirmed);
        revision.ResultThesisId.Should().Be(thesis.Id);
    }

    [Fact]
    public async Task ConfirmTriggerChange_ReplacesOnlyTheTriggers()
    {
        var thesis = Thesis(Guid.NewGuid());
        var wider = new ThesisInvalidationTrigger("price_drawdown", "greaterThan", 0.50m);
        var revision = Pending(ThesisRevisionKind.TriggerChange, thesis.Id, new ThesisRevisionContent(InvalidationTriggers: [wider]));
        SaveThesisCommand? sent = null;
        _save.Setup(h => h.Handle(It.IsAny<SaveThesisCommand>(), It.IsAny<CancellationToken>()))
            .Callback<SaveThesisCommand, CancellationToken>((c, _) => sent = c)
            .ReturnsAsync(new ThesisDto(thesis.Id, "CBRS", "Original case.", [], [], [wider], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null));

        await Handler().Handle(Confirm(revision), CancellationToken.None);

        sent!.InvalidationTriggers.Should().Equal(wider);
        sent.ThesisText.Should().Be("Original case.");
    }

    [Fact]
    public async Task ConfirmClose_DeletesTheThesis_AndJournalsClosedAsTheOwnersWrite()
    {
        var thesis = Thesis(Guid.NewGuid());
        var revision = Pending(ThesisRevisionKind.Close, thesis.Id);
        _delete.Setup(h => h.Handle(It.IsAny<DeleteThesisCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await Handler().Handle(Confirm(revision), CancellationToken.None);

        result.Outcome.Should().Be(ThesisRevisionOutcome.Confirmed);
        _delete.Verify(h => h.Handle(new DeleteThesisCommand(User, thesis.Id), It.IsAny<CancellationToken>()), Times.Once);
        _recorder.Verify(r => r.RecordAuditedAsync(
            User, ThesisSubjectType.Thesis, thesis.Id, "CBRS", ThesisEventType.Closed, ThesisEventOrigin.User,
            It.Is<ThesisAudit>(a => a.Prior!.ThesisText == "Original case."), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        revision.Status.Should().Be(ThesisRevisionStatus.Confirmed);
    }

    [Fact]
    public async Task Reject_NeedsAReason_KeepsItAsTheCounterfactual_AndWritesNothing()
    {
        var revision = Pending(ThesisRevisionKind.Promote);

        var noReason = () => Handler().Handle(
            new DecideThesisRevisionCommand(User, revision.Id, ThesisRevisionVerdict.Reject, "  "), CancellationToken.None);
        await noReason.Should().ThrowAsync<InvalidThesisRevisionException>();

        var result = await Handler().Handle(
            new DecideThesisRevisionCommand(User, revision.Id, ThesisRevisionVerdict.Reject, "Too crowded for me."), CancellationToken.None);

        result.Outcome.Should().Be(ThesisRevisionOutcome.Rejected);
        revision.Status.Should().Be(ThesisRevisionStatus.Rejected);
        revision.DecisionNote.Should().Be("Too crowded for me.");
        NothingWasWritten();
    }

    [Fact]
    public async Task ADecidedOrExpiredProposal_CannotBeDecided()
    {
        var decided = Pending(ThesisRevisionKind.Close);
        decided.Status = ThesisRevisionStatus.Rejected;
        var stale = Pending(ThesisRevisionKind.Close, expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        var again = () => Handler().Handle(Confirm(decided), CancellationToken.None);
        var late = () => Handler().Handle(Confirm(stale), CancellationToken.None);

        await again.Should().ThrowAsync<ThesisRevisionNotPendingException>();
        await late.Should().ThrowAsync<ThesisRevisionNotPendingException>();
        stale.Status.Should().Be(ThesisRevisionStatus.Expired);
        NothingWasWritten();
    }

    [Theory]
    [InlineData(ThesisRevisionVerdict.Confirm)]
    [InlineData(ThesisRevisionVerdict.Reject)]
    public async Task ADecisionThatLosesTheClaimToAnOverlappingOne_WritesNothing(ThesisRevisionVerdict verdict)
    {
        var revision = Pending(ThesisRevisionKind.Promote);
        var repo = new Mock<IThesisRevisionRepository>();
        repo.Setup(r => r.GetAsync(User, revision.Id, It.IsAny<CancellationToken>())).ReturnsAsync(revision);
        repo.Setup(r => r.TryClaimAsync(
                User, revision.Id, It.IsAny<ThesisRevisionStatus>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var audit = new Mock<IAuditActor>();
        audit.SetupGet(a => a.Kind).Returns(AuditActorKind.OwnerSession);
        var handler = new DecideThesisRevisionCommandHandler(
            repo.Object, _theses.Object, _promote.Object, _save.Object, _delete.Object, _recorder.Object, audit.Object);

        var act = () => handler.Handle(new DecideThesisRevisionCommand(User, revision.Id, verdict, "ok"), CancellationToken.None);

        await act.Should().ThrowAsync<ThesisRevisionNotPendingException>();
        revision.Status.Should().Be(ThesisRevisionStatus.Pending);
        NothingWasWritten();
    }

    [Fact]
    public async Task AConfirmThatFails_ReleasesTheClaim_SoTheOwnerCanRetry()
    {
        var revision = Pending(ThesisRevisionKind.Promote);
        _promote.Setup(h => h.Handle(It.IsAny<PromoteCandidateCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var act = () => Handler().Handle(Confirm(revision), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        revision.Status.Should().Be(ThesisRevisionStatus.Pending);
        revision.DecidedAt.Should().BeNull();
    }

    [Fact]
    public async Task AnUnknownProposal_Is404()
    {
        var act = () => Handler().Handle(new DecideThesisRevisionCommand(User, Guid.NewGuid(), ThesisRevisionVerdict.Confirm, null), CancellationToken.None);

        await act.Should().ThrowAsync<ThesisRevisionNotFoundException>();
    }

    [Fact]
    public async Task ExpiryJobCommand_ExpiresOnlyPendingProposalsPastTheirDate()
    {
        var stale = Pending(ThesisRevisionKind.Close, expiresAt: DateTimeOffset.UtcNow.AddDays(-1));
        var live = Pending(ThesisRevisionKind.Close);

        var count = await new ExpireThesisRevisionsCommandHandler(_revisions)
            .Handle(new ExpireThesisRevisionsCommand(DateTimeOffset.UtcNow), CancellationToken.None);

        count.Should().Be(1);
        stale.Status.Should().Be(ThesisRevisionStatus.Expired);
        live.Status.Should().Be(ThesisRevisionStatus.Pending);
    }
}
