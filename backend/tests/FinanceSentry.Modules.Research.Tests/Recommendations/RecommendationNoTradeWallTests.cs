namespace FinanceSentry.Modules.Research.Tests.Recommendations;

using System.Security.Claims;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Controllers;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Recommendations;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Tests.Opportunity;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

/// <summary>
/// The no-trade wall (#1069): Finance Sentry never places, stages or transmits an order. A confirm only records the
/// owner's decision and shows order text they place at the broker themselves.
/// </summary>
public sealed class RecommendationNoTradeWallTests
{
    private const string GrabOrder = "SELL 656 GRAB / LIMIT 3.13 USD DAY / IBKR";
    private static readonly Guid User = Guid.NewGuid();

    private static Recommendation Pending(RecommendationAction action = RecommendationAction.Trim) => new()
    {
        UserId = User,
        Ticker = "GRAB",
        Action = action,
        Origin = RecommendationOrigin.Rules,
        TriggerKind = RecommendationTriggerKind.RiskRule,
        TriggerRef = RecommendationEvidenceFactory.MaxPositionWeightRule,
        Rationale = "Over the cap.",
        InvalidationTrigger = "Back under the cap.",
        OrderText = GrabOrder,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(14),
    };

    [Fact]
    public async Task Confirming_records_the_decision_and_returns_the_exact_order_text_and_nothing_else_happens()
    {
        var pending = Pending();
        var store = new Mock<IRecommendationRepository>(MockBehavior.Strict);
        store.Setup(s => s.GetAsync(User, pending.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pending);
        store.Setup(s => s.UpdateAsync(pending, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var journal = new Mock<IThesisEventRecorder>(MockBehavior.Strict);
        journal.Setup(j => j.RecordAsync(
            User, ThesisSubjectType.Recommendation, pending.Id, "GRAB", ThesisEventType.Confirmed, "placed", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var dto = await new DecideRecommendationCommandHandler(store.Object, journal.Object).Handle(
            new DecideRecommendationCommand(User, pending.Id, RecommendationVerdict.Confirm, "placed"), CancellationToken.None);

        dto.Status.Should().Be("Confirmed");
        dto.OrderText.Should().Be(GrabOrder);
        dto.Execution.Should().Be("Pending", "the owner places the order; the outcome job only watches for their fill");
        store.VerifyAll();
        journal.VerifyAll();
        store.VerifyNoOtherCalls();
        journal.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task The_confirm_endpoint_answers_with_the_order_text_the_owner_places_themselves()
    {
        var repo = new InMemoryRecommendationRepository();
        var pending = Pending();
        repo.Rows.Add(pending);
        var journal = new JournalRecorder();
        var controller = new RecommendationsController(
            new GetRecommendationsQueryHandler(repo), new DecideRecommendationCommandHandler(repo, journal))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", User.ToString())], "test")),
                },
            },
        };

        var result = await controller.Confirm(pending.Id, new ConfirmRecommendationRequest(null), CancellationToken.None);

        var dto = result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<RecommendationDto>().Subject;
        dto.OrderText.Should().Be(GrabOrder);
        dto.Status.Should().Be("Confirmed");
        journal.Events.Should().ContainSingle().Which.EventType.Should().Be(ThesisEventType.Confirmed);
    }

    [Fact]
    public async Task A_proposal_confirmed_after_being_queued_changes_nothing_but_its_own_status_and_journal()
    {
        var repo = new InMemoryRecommendationRepository();
        var journal = new JournalRecorder();
        var book = RecommendationTestBook.With(76_000m, RecommendationTestBook.Stock("GRAB", 2_962m, 9_272m));
        var propose = new ProposeRecommendationCommandHandler(
            new Mock<IThesisRepository>(MockBehavior.Strict).Object,
            new FakePositionCapSource(0.095m),
            new Mock<IAllocationDriftReader>(MockBehavior.Strict).Object,
            new Mock<IAssetSignalReader>(MockBehavior.Strict).Object,
            new Mock<IFiredEventSource>(MockBehavior.Strict).Object,
            new FakeRiskPolicyGate(new RiskGateVerdict(RiskGateDecision.Allowed, null, null, null, null)),
            RecommendationTestBook.Reader(book, new Dictionary<string, decimal> { ["GRAB"] = 3.13m }),
            new RecommendationWriter(repo, journal));
        var queued = await propose.Handle(
            new ProposeRecommendationCommand(
                User, "GRAB", RecommendationAction.Trim, RecommendationTriggerKind.RiskRule, string.Empty,
                "GRAB is 12.2% of book against a 9.5% cap.", RecommendationConfidence.High, "Back under the cap.", []),
            CancellationToken.None);

        var decided = await new DecideRecommendationCommandHandler(repo, journal).Handle(
            new DecideRecommendationCommand(User, queued.Id, RecommendationVerdict.Confirm, null), CancellationToken.None);

        decided.OrderText.Should().Be(GrabOrder).And.Be(queued.OrderText);
        repo.Rows.Should().ContainSingle().Which.Status.Should().Be(RecommendationStatus.Confirmed);
        journal.Events.Select(e => e.EventType).Should().Equal(ThesisEventType.Created, ThesisEventType.Confirmed);
    }
}
