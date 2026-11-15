namespace FinanceSentry.Modules.Research.Tests.Scorecards;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FluentAssertions;
using Xunit;

public sealed class EtfVerdictReasonsTests
{
    private static readonly VerdictOptions Options = new();

    [Fact]
    public void ABuy_HasOneReasonPerAxis_AndNoTurnsBuyLine()
    {
        var explanation = Explain(60, CrowdingClass.Normal, 70m);

        explanation.Reasons.Select(r => r.Axis).Should().Equal("Structure", "Crowding", "LookThrough");
        explanation.Reasons.Should().OnlyContain(r => r.Status == VerdictReasonStatus.Pass);
        explanation.Reasons.Single(r => r.Axis == "LookThrough").Text
            .Should().Be("70% of the top-10 weight reads BUY or better, above the 50% BUY needs.");
        explanation.TurnsBuyWhen.Should().BeNull();
    }

    [Fact]
    public void AWaitOnTheLookThrough_NamesTheShareAndWhatWouldTurnItIntoBuy()
    {
        var explanation = Explain(60, CrowdingClass.Normal, 40m);

        var lookThrough = explanation.Reasons.Single(r => r.Axis == "LookThrough");
        lookThrough.Status.Should().Be(VerdictReasonStatus.Fail);
        lookThrough.Decisive.Should().BeTrue();
        lookThrough.Text.Should().Be("40% of the top-10 weight reads BUY or better; BUY needs more than 50%.");
        explanation.Reasons.Where(r => r.Axis != "LookThrough").Should().OnlyContain(r => !r.Decisive);
        explanation.TurnsBuyWhen.Should().Be("more than 50% of the top-10 weight reads BUY or better (reads 40%)");
    }

    [Fact]
    public void MissingHoldings_AreAMissingReason_NotAFailure()
    {
        var explanation = Explain(60, CrowdingClass.Normal, null);

        var lookThrough = explanation.Reasons.Single(r => r.Axis == "LookThrough");
        lookThrough.Status.Should().Be(VerdictReasonStatus.Missing);
        lookThrough.Text.Should().Contain("could not be read, so BUY is out of reach");
        explanation.TurnsBuyWhen.Should().Contain("top holdings can be read");
    }

    [Fact]
    public void AnAvoid_NamesTheFundStructureAndEveryBarItMisses()
    {
        var explanation = Explain(22, CrowdingClass.Extended, 80m);

        explanation.Reasons.Single(r => r.Axis == "Structure").Text.Should().Be("Fund structure 22 is below the AVOID line of 30.");
        explanation.Reasons.Single(r => r.Axis == "Structure").Decisive.Should().BeTrue();
        explanation.TurnsBuyWhen.Should().Be("the fund's structure reaches 50 (reads 22) and the fund's crowding is no longer Extended");
    }

    [Fact]
    public void NotScored_HasNoTurnsBuyLine()
    {
        var explanation = Explain(null, null, null);

        explanation.Reasons.Should().OnlyContain(r => r.Status == VerdictReasonStatus.Missing);
        explanation.TurnsBuyWhen.Should().BeNull();
    }

    private static VerdictExplanation Explain(int? structure, CrowdingClass? crowding, decimal? share) =>
        VerdictReasons.Explain(EtfVerdictRules.Evaluate(new EtfVerdictInputs(structure, crowding, share), Options));
}
