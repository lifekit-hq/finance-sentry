namespace FinanceSentry.Modules.Research.Tests.ThesisRevisions;

using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Domain.Revisions;
using FluentAssertions;
using Moq;
using Xunit;

public sealed class ProposeThesisRevisionCommandTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly ThesisInvalidationTrigger Drawdown = new("price_drawdown", "greaterThan", 0.30m);

    private readonly InMemoryThesisRevisionRepository _revisions = new();
    private readonly Mock<IThesisRepository> _theses = new();
    private readonly Mock<ICandidateRepository> _candidates = new();

    private ProposeThesisRevisionCommandHandler Handler() => new(_theses.Object, _candidates.Object, _revisions);

    private InvestmentThesis Thesis()
    {
        var thesis = new InvestmentThesis
        {
            UserId = User,
            Ticker = "CBRS",
            ThesisText = "Original case.",
            InvalidationTriggers = [Drawdown],
        };
        _theses.Setup(r => r.FindAsync(User, thesis.Id, It.IsAny<CancellationToken>())).ReturnsAsync(thesis);
        return thesis;
    }

    private OpportunityCandidate Candidate(CandidateStatus status = CandidateStatus.Active)
    {
        var candidate = new OpportunityCandidate { UserId = User, Ticker = "AVGO", Status = status };
        _candidates.Setup(r => r.GetAsync(User, candidate.Id, It.IsAny<CancellationToken>())).ReturnsAsync(candidate);
        return candidate;
    }

    private static ProposeThesisRevisionCommand Propose(
        ThesisRevisionKind kind, Guid subject, ThesisRevisionContent? content = null, string rationale = "Q3 print changed the case.")
        => new(User, kind, subject, rationale, content ?? new ThesisRevisionContent());

    [Fact]
    public async Task Promote_OfAnActiveCandidate_QueuesAPendingProposalWritingNothing()
    {
        var candidate = Candidate();

        var dto = await Handler().Handle(
            Propose(ThesisRevisionKind.Promote, candidate.Id, new ThesisRevisionContent(ThesisText: "  Case.  ", InvalidationTriggers: [Drawdown])),
            CancellationToken.None);

        dto.Status.Should().Be("Pending");
        dto.ProposedBy.Should().Be("Ledger");
        dto.Ticker.Should().Be("AVGO");
        dto.SubjectType.Should().Be("Candidate");
        dto.Proposed.ThesisText.Should().Be("Case.");
        dto.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(ThesisRevision.DefaultExpiryDays), TimeSpan.FromMinutes(1));
        _theses.Verify(r => r.UpsertAsync(It.IsAny<InvestmentThesis>(), It.IsAny<CancellationToken>()), Times.Never);
        _candidates.Verify(r => r.UpdateAsync(It.IsAny<OpportunityCandidate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(CandidateStatus.Promoted)]
    [InlineData(CandidateStatus.Rejected)]
    [InlineData(CandidateStatus.Expired)]
    public async Task Promote_OfACandidateThatIsNotActive_IsRefused(CandidateStatus status)
    {
        var candidate = Candidate(status);

        var act = () => Handler().Handle(Propose(ThesisRevisionKind.Promote, candidate.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidThesisRevisionException>();
        _revisions.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task ANewerProposalOnTheSameSubject_SupersedesTheOpenOne()
    {
        var thesis = Thesis();
        var first = await Handler().Handle(
            Propose(ThesisRevisionKind.NarrativeUpdate, thesis.Id, new ThesisRevisionContent(ThesisText: "First.")), CancellationToken.None);

        var second = await Handler().Handle(
            Propose(ThesisRevisionKind.NarrativeUpdate, thesis.Id, new ThesisRevisionContent(ThesisText: "Second.")), CancellationToken.None);

        var rows = _revisions.Rows.ToDictionary(r => r.Id);
        rows[first.Id].Status.Should().Be(ThesisRevisionStatus.Superseded);
        rows[first.Id].SupersededById.Should().Be(second.Id);
        rows[second.Id].Status.Should().Be(ThesisRevisionStatus.Pending);
    }

    [Fact]
    public async Task APendingProposalPastItsExpiry_IsSettledAsExpired_AndDoesNotBlockANewOne()
    {
        var thesis = Thesis();
        var stale = await Handler().Handle(
            Propose(ThesisRevisionKind.NarrativeUpdate, thesis.Id, new ThesisRevisionContent(ThesisText: "First.")), CancellationToken.None);
        _revisions.Rows.Single(r => r.Id == stale.Id).ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);

        var fresh = await Handler().Handle(
            Propose(ThesisRevisionKind.NarrativeUpdate, thesis.Id, new ThesisRevisionContent(ThesisText: "Second.")), CancellationToken.None);

        var rows = _revisions.Rows.ToDictionary(r => r.Id);
        rows[stale.Id].Status.Should().Be(ThesisRevisionStatus.Expired);
        rows[stale.Id].SupersededById.Should().BeNull();
        rows[fresh.Id].Status.Should().Be(ThesisRevisionStatus.Pending);
    }

    [Fact]
    public async Task ARationale_IsRequired()
    {
        var thesis = Thesis();

        var act = () => Handler().Handle(
            Propose(ThesisRevisionKind.Close, thesis.Id, rationale: "   "), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidThesisRevisionException>().WithMessage("*rationale*");
    }

    [Fact]
    public async Task NarrativeUpdate_ThatChangesTriggers_OrChangesNothing_IsRefused()
    {
        var thesis = Thesis();

        var withTriggers = () => Handler().Handle(
            Propose(ThesisRevisionKind.NarrativeUpdate, thesis.Id, new ThesisRevisionContent(InvalidationTriggers: [Drawdown])), CancellationToken.None);
        var noChange = () => Handler().Handle(
            Propose(ThesisRevisionKind.NarrativeUpdate, thesis.Id, new ThesisRevisionContent(ThesisText: "Original case.")), CancellationToken.None);

        await withTriggers.Should().ThrowAsync<InvalidThesisRevisionException>().WithMessage("*TriggerChange*");
        await noChange.Should().ThrowAsync<InvalidThesisRevisionException>().WithMessage("*nothing*");
    }

    [Fact]
    public async Task TriggerChange_NeedsTriggersFromTheClosedVocabulary_AndOnlyTriggers()
    {
        var thesis = Thesis();

        var none = () => Handler().Handle(Propose(ThesisRevisionKind.TriggerChange, thesis.Id), CancellationToken.None);
        var unknown = () => Handler().Handle(
            Propose(ThesisRevisionKind.TriggerChange, thesis.Id,
                new ThesisRevisionContent(InvalidationTriggers: [new ThesisInvalidationTrigger("vibes", "lessThan", 1m)])),
            CancellationToken.None);
        var mixed = () => Handler().Handle(
            Propose(ThesisRevisionKind.TriggerChange, thesis.Id,
                new ThesisRevisionContent(ThesisText: "Also this.", InvalidationTriggers: [new ThesisInvalidationTrigger("price_drawdown", "greaterThan", 0.5m)])),
            CancellationToken.None);

        await none.Should().ThrowAsync<InvalidThesisRevisionException>();
        await unknown.Should().ThrowAsync<Exception>();
        await mixed.Should().ThrowAsync<InvalidThesisRevisionException>();
    }

    [Fact]
    public async Task Close_CarriesNoContent()
    {
        var thesis = Thesis();

        var act = () => Handler().Handle(
            Propose(ThesisRevisionKind.Close, thesis.Id, new ThesisRevisionContent(ThesisText: "x")), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidThesisRevisionException>();
        var ok = await Handler().Handle(Propose(ThesisRevisionKind.Close, thesis.Id), CancellationToken.None);
        ok.Kind.Should().Be("Close");
    }

    [Fact]
    public async Task AThesisThatDoesNotExist_IsRefused()
    {
        var act = () => Handler().Handle(Propose(ThesisRevisionKind.Close, Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<ThesisNotFoundException>();
    }
}
