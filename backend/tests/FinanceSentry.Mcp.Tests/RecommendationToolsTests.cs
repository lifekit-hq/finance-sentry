using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using FinanceSentry.Modules.Research.Domain.Recommendations;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

/// <summary>#1069: the Ledger proposes, lists and records the owner's verdict; none of the three tools can reach a broker.</summary>
public sealed class RecommendationToolsTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<ICommandHandler<ProposeRecommendationCommand, RecommendationDto>> _propose = new();
    private readonly Mock<ICommandHandler<DecideRecommendationCommand, RecommendationDto>> _decide = new();
    private readonly Mock<IQueryHandler<GetRecommendationsQuery, RecommendationsResult>> _list = new();

    private static FakeIdentityResolver Identity(Guid? userId) => new() { ResolvedUserId = userId };

    [Fact]
    public async Task Every_tool_returns_null_without_an_identity()
    {
        (await new ProposeRecommendationTool(_propose.Object, Identity(null))
            .ExecuteAsync("GRAB", "Trim", "RiskRule", null, "why", "High", "when")).Should().BeNull();
        (await new DecideRecommendationTool(_decide.Object, Identity(null)).ExecuteAsync(Guid.NewGuid(), "Confirm")).Should().BeNull();
        (await new ListRecommendationsTool(_list.Object, Identity(null)).ExecuteAsync()).Should().BeNull();

        _propose.VerifyNoOtherCalls();
        _decide.VerifyNoOtherCalls();
        _list.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Propose_parses_the_enums_case_insensitively_and_passes_the_identity()
    {
        ProposeRecommendationCommand? captured = null;
        _propose.Setup(h => h.Handle(It.IsAny<ProposeRecommendationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ProposeRecommendationCommand, CancellationToken>((c, _) => captured = c)
            .ReturnsAsync((RecommendationDto)null!);

        await new ProposeRecommendationTool(_propose.Object, Identity(UserId))
            .ExecuteAsync("GRAB", " trim ", "riskrule", null, "12.2% of book", "high", "Back under the cap", sizeUsd: 2_000m);

        captured!.UserId.Should().Be(UserId);
        captured.Action.Should().Be(RecommendationAction.Trim);
        captured.TriggerKind.Should().Be(RecommendationTriggerKind.RiskRule);
        captured.Confidence.Should().Be(RecommendationConfidence.High);
        captured.TriggerRef.Should().BeEmpty();
        captured.Evidence.Should().BeEmpty();
        captured.SizeUsd.Should().Be(2_000m);
    }

    [Theory]
    [InlineData("Buy", "RiskRule", "High")]
    [InlineData("Trim", "Gut", "High")]
    [InlineData("Trim", "RiskRule", "Certain")]
    [InlineData("7", "RiskRule", "High")]
    public async Task Propose_refuses_values_outside_the_agreed_vocabulary(string action, string trigger, string confidence)
    {
        var act = () => new ProposeRecommendationTool(_propose.Object, Identity(UserId))
            .ExecuteAsync("GRAB", action, trigger, null, "why", confidence, "when");

        await act.Should().ThrowAsync<InvalidRecommendationException>();
        _propose.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Decide_passes_the_verdict_and_note()
    {
        var id = Guid.NewGuid();
        DecideRecommendationCommand? captured = null;
        _decide.Setup(h => h.Handle(It.IsAny<DecideRecommendationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<DecideRecommendationCommand, CancellationToken>((c, _) => captured = c)
            .ReturnsAsync((RecommendationDto)null!);

        await new DecideRecommendationTool(_decide.Object, Identity(UserId)).ExecuteAsync(id, "reject", "Cycle has a year to run");

        captured.Should().Be(new DecideRecommendationCommand(UserId, id, RecommendationVerdict.Reject, "Cycle has a year to run"));
    }

    [Theory]
    [InlineData("Execute")]
    [InlineData("Place")]
    [InlineData("")]
    public async Task Decide_knows_only_confirm_and_reject(string verdict)
    {
        var act = () => new DecideRecommendationTool(_decide.Object, Identity(UserId)).ExecuteAsync(Guid.NewGuid(), verdict);

        await act.Should().ThrowAsync<InvalidRecommendationException>();
    }

    [Theory]
    [InlineData(typeof(ProposeRecommendationTool))]
    [InlineData(typeof(DecideRecommendationTool))]
    [InlineData(typeof(ListRecommendationsTool))]
    public void The_tools_reach_only_their_own_handler_and_the_identity(Type tool)
        => tool.GetConstructors().Single().GetParameters().Select(p => p.ParameterType)
            .Should().HaveCount(2)
            .And.Contain(typeof(IIdentityResolver))
            .And.Contain(t => t.IsGenericType && t.GetGenericArguments()[0].Namespace!.StartsWith("FinanceSentry.Modules.Research.Application", StringComparison.Ordinal)
                && t.GetGenericArguments()[0].Name.Contains("Recommendation"));
}
