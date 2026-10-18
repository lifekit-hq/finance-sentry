namespace FinanceSentry.Modules.Research.Tests.Recommendations;

using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Domain.Recommendations;
using FluentAssertions;
using Xunit;

/// <summary>
/// #986: the decision-value rule moved from the Decisions page to the server; these are the page's former cases, so the
/// value the page now reads is the one it used to compute.
/// </summary>
public sealed class RecommendationValueTests
{
    private static Recommendation Scored(
        RecommendationAction action,
        RecommendationStatus status,
        RecommendationExecution execution,
        decimal subjectReturnPct,
        decimal benchmarkReturnPct)
        => new()
        {
            Ticker = "GRAB",
            Action = action,
            Status = status,
            Execution = execution,
            SubjectReturnPct = subjectReturnPct,
            BenchmarkReturnPct = benchmarkReturnPct,
        };

    [Theory]
    [InlineData(RecommendationAction.Add, RecommendationStatus.Confirmed, RecommendationExecution.Executed, 3.21, 3.21)]
    [InlineData(RecommendationAction.Hold, RecommendationStatus.Confirmed, RecommendationExecution.NotApplicable, -1.5, -1.5)]
    [InlineData(RecommendationAction.Exit, RecommendationStatus.Confirmed, RecommendationExecution.Executed, -5.3, 5.3)]
    [InlineData(RecommendationAction.Trim, RecommendationStatus.Confirmed, RecommendationExecution.Executed, 2, -2)]
    [InlineData(RecommendationAction.Trim, RecommendationStatus.Confirmed, RecommendationExecution.Executed, 0, 0)]
    [InlineData(RecommendationAction.Exit, RecommendationStatus.Rejected, RecommendationExecution.NotApplicable, 5.3, 5.3)]
    [InlineData(RecommendationAction.Trim, RecommendationStatus.Rejected, RecommendationExecution.NotApplicable, -1, -1)]
    [InlineData(RecommendationAction.Add, RecommendationStatus.Rejected, RecommendationExecution.NotApplicable, 4, -4)]
    public void DecisionValue_SignsTheExcessByTheSideTheDecisionLeftTheOwnerOn(
        RecommendationAction action, RecommendationStatus status, RecommendationExecution execution, double excess, double expected)
    {
        var r = Scored(action, status, execution, 1m + (decimal)excess, 1m);

        RecommendationValue.ExcessReturnPct(r).Should().Be((decimal)excess);
        RecommendationValue.DecisionValuePct(r).Should().Be((decimal)expected);
    }

    [Theory]
    [InlineData(RecommendationAction.Hold, RecommendationStatus.Rejected, RecommendationExecution.NotApplicable)]
    [InlineData(RecommendationAction.Exit, RecommendationStatus.Confirmed, RecommendationExecution.Unexecuted)]
    [InlineData(RecommendationAction.Add, RecommendationStatus.Expired, RecommendationExecution.NotApplicable)]
    [InlineData(RecommendationAction.Add, RecommendationStatus.Superseded, RecommendationExecution.NotApplicable)]
    public void DecisionValue_IsNull_WhereThereIsNoDecisionToSign(
        RecommendationAction action, RecommendationStatus status, RecommendationExecution execution)
    {
        var r = Scored(action, status, execution, 3m, 1m);

        RecommendationValue.DecisionValuePct(r).Should().BeNull();
    }

    [Fact]
    public void DecisionValue_IsNull_UntilScored()
    {
        var r = new Recommendation { Action = RecommendationAction.Add, Status = RecommendationStatus.Confirmed, Execution = RecommendationExecution.Executed };

        RecommendationValue.ExcessReturnPct(r).Should().BeNull();
        RecommendationValue.DecisionValuePct(r).Should().BeNull();
        RecommendationValue.ProposalValuePct(r).Should().BeNull();
    }

    [Theory]
    [InlineData(RecommendationAction.Add, RecommendationStatus.Rejected, 4)]
    [InlineData(RecommendationAction.Hold, RecommendationStatus.Rejected, 4)]
    [InlineData(RecommendationAction.Exit, RecommendationStatus.Rejected, -4)]
    [InlineData(RecommendationAction.Trim, RecommendationStatus.Confirmed, -4)]
    public void ProposalValue_SignsByTheProposalWhateverTheOwnerDecided(
        RecommendationAction action, RecommendationStatus status, double expected)
    {
        var r = Scored(action, status, RecommendationExecution.NotApplicable, 5m, 1m);

        RecommendationValue.ProposalValuePct(r).Should().Be((decimal)expected);
    }

    [Fact]
    public void TheDto_CarriesTheServerDecisionValue()
    {
        var r = Scored(RecommendationAction.Exit, RecommendationStatus.Confirmed, RecommendationExecution.Executed, -4.1m, 1.2m);

        var dto = RecommendationDto.From(r);

        dto.ExcessReturnPct.Should().Be(-5.3m);
        dto.DecisionValuePct.Should().Be(5.3m);
    }
}
