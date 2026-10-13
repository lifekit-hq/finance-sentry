namespace FinanceSentry.Modules.Research.Tests.Recommendations;

using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using FinanceSentry.Modules.Research.Domain.Recommendations;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public sealed class DecideRecommendationCommandTests
{
    private static readonly Guid User = Guid.NewGuid();

    private readonly InMemoryRecommendationRepository _repo = new();
    private readonly JournalRecorder _journal = new();
    private readonly Mock<IThesisRepository> _theses = new();

    private DecideRecommendationCommandHandler Handler() => new(_repo, _theses.Object, _journal);

    private Recommendation Pending(RecommendationAction action = RecommendationAction.Trim, DateTimeOffset? expiresAt = null)
    {
        var r = new Recommendation
        {
            UserId = User,
            Ticker = "GRAB",
            Action = action,
            Origin = RecommendationOrigin.Rules,
            TriggerKind = RecommendationTriggerKind.RiskRule,
            TriggerRef = RecommendationEvidenceFactory.MaxPositionWeightRule,
            Rationale = "Over the cap.",
            InvalidationTrigger = "Back under the cap.",
            OrderText = "SELL 656 GRAB / LIMIT 3.13 USD DAY / IBKR",
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddDays(14),
        };
        _repo.Rows.Add(r);
        return r;
    }

    private void HasThesis(out InvestmentThesis thesis)
    {
        thesis = new InvestmentThesis { UserId = User, Ticker = "GRAB" };
        _theses.Setup(t => t.FindByTickerAsync(User, "GRAB", It.IsAny<CancellationToken>())).ReturnsAsync([thesis]);
    }

    [Fact]
    public async Task Confirm_records_the_decision_and_journals_it_without_anything_else()
    {
        var r = Pending();
        _theses.Setup(t => t.FindByTickerAsync(User, "GRAB", It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var dto = await Handler().Handle(new DecideRecommendationCommand(User, r.Id, RecommendationVerdict.Confirm, "  Placed at the open "), CancellationToken.None);

        dto.Status.Should().Be("Confirmed");
        dto.DecisionNote.Should().Be("Placed at the open");
        dto.Execution.Should().Be("Pending", "the outcome job watches for the owner's own fill");
        dto.OrderText.Should().Be("SELL 656 GRAB / LIMIT 3.13 USD DAY / IBKR");
        _journal.Events.Should().ContainSingle()
            .Which.Should().Be((ThesisSubjectType.Recommendation, r.Id, ThesisEventType.Confirmed, "Placed at the open"));
    }

    [Fact]
    public async Task Confirming_an_exit_closes_the_tickers_thesis_in_the_journal()
    {
        var r = Pending(RecommendationAction.Exit);
        HasThesis(out var thesis);

        await Handler().Handle(new DecideRecommendationCommand(User, r.Id, RecommendationVerdict.Confirm, null), CancellationToken.None);

        _journal.Events.Should().Contain(e => e.SubjectType == ThesisSubjectType.Thesis
            && e.SubjectId == thesis.Id && e.EventType == ThesisEventType.Closed);
    }

    [Fact]
    public async Task Confirming_a_trim_marks_the_thesis_trimmed()
    {
        var r = Pending();
        HasThesis(out var thesis);

        await Handler().Handle(new DecideRecommendationCommand(User, r.Id, RecommendationVerdict.Confirm, null), CancellationToken.None);

        _journal.Events.Should().Contain(e => e.SubjectId == thesis.Id && e.EventType == ThesisEventType.Trimmed);
    }

    [Fact]
    public async Task Reject_keeps_the_reason_as_the_counterfactual_and_never_touches_the_thesis()
    {
        var r = Pending(RecommendationAction.Exit);
        HasThesis(out _);

        var dto = await Handler().Handle(
            new DecideRecommendationCommand(User, r.Id, RecommendationVerdict.Reject, "DRAM cycle has a year to run"), CancellationToken.None);

        dto.Status.Should().Be("Rejected");
        dto.Execution.Should().Be("NotApplicable");
        _journal.Events.Should().ContainSingle()
            .Which.Should().Be((ThesisSubjectType.Recommendation, r.Id, ThesisEventType.Rejected, "DRAM cycle has a year to run"));
    }

    [Fact]
    public async Task Reject_without_a_reason_is_refused()
    {
        var r = Pending();

        var act = () => Handler().Handle(new DecideRecommendationCommand(User, r.Id, RecommendationVerdict.Reject, "  "), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidRecommendationException>().WithMessage("*why*");
        r.Status.Should().Be(RecommendationStatus.Pending);
    }

    [Fact]
    public async Task A_decided_proposal_cannot_be_decided_again()
    {
        var r = Pending();
        r.Status = RecommendationStatus.Rejected;

        var act = () => Handler().Handle(new DecideRecommendationCommand(User, r.Id, RecommendationVerdict.Confirm, null), CancellationToken.None);

        await act.Should().ThrowAsync<RecommendationNotPendingException>();
    }

    [Fact]
    public async Task Another_persons_proposal_is_not_found()
    {
        var r = Pending();

        var act = () => Handler().Handle(new DecideRecommendationCommand(Guid.NewGuid(), r.Id, RecommendationVerdict.Confirm, null), CancellationToken.None);

        await act.Should().ThrowAsync<RecommendationNotFoundException>();
    }

    [Fact]
    public async Task Deciding_a_proposal_past_its_expiry_expires_it_in_the_journal_instead()
    {
        var r = Pending(expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        var act = () => Handler().Handle(new DecideRecommendationCommand(User, r.Id, RecommendationVerdict.Confirm, null), CancellationToken.None);

        await act.Should().ThrowAsync<RecommendationNotPendingException>();
        r.Status.Should().Be(RecommendationStatus.Expired);
        _journal.Events.Should().ContainSingle(e => e.EventType == ThesisEventType.Expired);
    }

    [Fact]
    public async Task The_expiry_sweep_expires_and_journals_every_overdue_proposal()
    {
        var overdue = Pending(expiresAt: DateTimeOffset.UtcNow.AddDays(-1));
        var live = new Recommendation { UserId = Guid.NewGuid(), Ticker = "MU", ExpiresAt = DateTimeOffset.UtcNow.AddDays(3) };
        _repo.Rows.Add(live);

        var count = await new ExpireRecommendationsCommandHandler(_repo, _journal)
            .Handle(new ExpireRecommendationsCommand(DateTimeOffset.UtcNow), CancellationToken.None);

        count.Should().Be(1);
        overdue.Status.Should().Be(RecommendationStatus.Expired);
        live.Status.Should().Be(RecommendationStatus.Pending);
        _journal.Events.Should().ContainSingle(e => e.SubjectId == overdue.Id && e.EventType == ThesisEventType.Expired);
    }

    [Fact]
    public async Task The_queue_lists_pending_oldest_first_and_what_closed_in_the_last_thirty_days()
    {
        var newer = Pending();
        newer.CreatedAt = DateTimeOffset.UtcNow.AddHours(-1);
        var older = new Recommendation { UserId = User, Ticker = "DRAM", CreatedAt = DateTimeOffset.UtcNow.AddDays(-2), ExpiresAt = DateTimeOffset.UtcNow.AddDays(5) };
        var recent = new Recommendation { UserId = User, Ticker = "MU", Status = RecommendationStatus.Rejected, DecidedAt = DateTimeOffset.UtcNow.AddDays(-3) };
        var old = new Recommendation { UserId = User, Ticker = "AAPL", Status = RecommendationStatus.Confirmed, DecidedAt = DateTimeOffset.UtcNow.AddDays(-45) };
        _repo.Rows.AddRange([older, recent, old]);

        var result = await new GetRecommendationsQueryHandler(_repo).Handle(new GetRecommendationsQuery(User), CancellationToken.None);

        result.Pending.Select(p => p.Ticker).Should().Equal("DRAM", "GRAB");
        result.Decided.Select(p => p.Ticker).Should().Equal("MU");
    }
}
