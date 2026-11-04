namespace FinanceSentry.Modules.Research.Tests.Scorecards;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FluentAssertions;
using Xunit;

/// <summary>
/// The verdict block's reasons and its "turns BUY when" line are derived from the rule table the evaluator
/// returned: one reason per axis with the value against the bar, nothing model-written, and no rule or
/// threshold of their own.
/// </summary>
public sealed class VerdictReasonsTests
{
    private static readonly VerdictOptions Options = new();

    [Fact]
    public void Explain_ScoredName_GivesOneReasonPerAxisInFixedOrder()
    {
        var explanation = Explain(78, 72, CrowdingClass.Normal, 8.5);

        explanation.Reasons.Select(r => r.Axis).Should().Equal("Structure", "Fundamentals", "Crowding", "Valuation");
        explanation.Reasons.Count.Should().BeInRange(3, 5);
    }

    [Fact]
    public void Explain_StrongBuy_AllPass_AndHasNoTurnsBuyLine()
    {
        var explanation = Explain(78, 72, CrowdingClass.Normal, 8.5);

        explanation.Reasons.Should().OnlyContain(r => r.Status == VerdictReasonStatus.Pass);
        explanation.Reasons[0].Text.Should().Be("Structure 78 clears the BUY bar of 50 and the STRONG BUY bar of 70.");
        explanation.Reasons[1].Text.Should().Be("Fundamentals 72 clear the BUY bar of 50 and the STRONG BUY bar of 70.");
        explanation.Reasons[2].Text.Should().Be("Crowding is Normal, not Extended.");
        explanation.Reasons[3].Text.Should().Be("P/E premium to its 5-year average is +8.5%, within the STRONG BUY limit of 10%.");
        explanation.TurnsBuyWhen.Should().BeNull("a name that is already BUY or better has nothing to turn");
    }

    [Fact]
    public void Explain_Buy_HasNoTurnsBuyLine_AndShowsWhatKeepsItFromStrongBuy()
    {
        var explanation = Explain(78, 72, CrowdingClass.Normal, 10.1);

        explanation.Reasons[3].Status.Should().Be(VerdictReasonStatus.Fail);
        explanation.Reasons[3].Text.Should().Contain("+10.1%").And.Contain("at most 10%");
        explanation.TurnsBuyWhen.Should().BeNull();
    }

    [Fact]
    public void Explain_WaitBelowTheBuyBar_NamesTheBarAndWhatItWouldTake()
    {
        var explanation = Explain(39, 78, CrowdingClass.Early, null);

        explanation.Reasons[0].Status.Should().Be(VerdictReasonStatus.Fail);
        explanation.Reasons[0].Text.Should().Be("Structure 39 is below the BUY bar of 50.");
        explanation.Reasons[0].Decisive.Should().BeTrue("the catch-all WAIT is decided by the BUY bar the name missed");
        explanation.Reasons[1].Status.Should().Be(VerdictReasonStatus.Pass);
        explanation.Reasons[1].Decisive.Should().BeFalse();
        explanation.Reasons[3].Status.Should().Be(VerdictReasonStatus.Missing);
        explanation.TurnsBuyWhen.Should().Be("structure reaches 50 (reads 39)");
    }

    [Fact]
    public void Explain_WaitOnBothBars_ListsBothInTheTurnsBuyLine()
    {
        var explanation = Explain(45, 35, CrowdingClass.Normal, 0.0);

        explanation.TurnsBuyWhen.Should().Be("structure reaches 50 (reads 45) and fundamentals reach 50 (reads 35)");
    }

    [Fact]
    public void Explain_Avoid_OnWeakStructure_FlagsTheAvoidLine_AndStillShowsTheOtherAxes()
    {
        var explanation = Explain(9, 75, CrowdingClass.Early, null);

        explanation.Reasons[0].Text.Should().Be("Structure 9 is below the AVOID line of 30.");
        explanation.Reasons[0].Decisive.Should().BeTrue();
        explanation.Reasons[1].Status.Should().Be(VerdictReasonStatus.Pass);
        explanation.TurnsBuyWhen.Should().Be("structure reaches 50 (reads 9)");
    }

    [Fact]
    public void Explain_Avoid_OnExtendedAndRich_NamesTheGuardInTheTurnsBuyLine()
    {
        var explanation = Explain(88, 80, CrowdingClass.Extended, 29.2);

        explanation.Reasons[2].Status.Should().Be(VerdictReasonStatus.Fail);
        explanation.Reasons[2].Decisive.Should().BeTrue();
        explanation.Reasons[3].Decisive.Should().BeTrue();
        explanation.TurnsBuyWhen.Should().Be(
            "crowding is no longer Extended or the P/E premium falls to 0% or below (reads +29.2%)");
    }

    [Fact]
    public void Explain_MissingStructure_SaysSoAndAsksForAReadableScore()
    {
        var explanation = Explain(null, 75, null, null);

        explanation.Reasons[0].Status.Should().Be(VerdictReasonStatus.Missing);
        explanation.TurnsBuyWhen.Should().Be("a structure score can be read and reaches 50");
    }

    [Fact]
    public void Explain_MissingFundamentals_IsNotARequirementForBuy()
    {
        var explanation = Explain(45, null, CrowdingClass.Normal, null);

        explanation.Reasons[1].Status.Should().Be(VerdictReasonStatus.Missing);
        explanation.Reasons[1].Text.Should().Contain("capped at BUY");
        explanation.TurnsBuyWhen.Should().Be("structure reaches 50 (reads 45)");
    }

    [Fact]
    public void Explain_NothingRead_MarksEveryAxisMissing()
    {
        var verdict = VerdictRules.Evaluate(new VerdictInputs(null, null, null, false, null), Options);

        var explanation = VerdictReasons.Explain(verdict);

        verdict.FiredRuleId.Should().Be(VerdictRules.NoAxisData);
        explanation.Reasons.Should().HaveCount(4).And.OnlyContain(r => r.Status == VerdictReasonStatus.Missing);
        explanation.TurnsBuyWhen.Should().BeNull("a name that is not scored has no BUY to turn into");
    }

    [Fact]
    public void Explain_OutsideTheUniverse_GivesTheOneReason_AndNoTurnsBuyLine()
    {
        var verdict = VerdictRules.Evaluate(new VerdictInputs(85, null, CrowdingClass.Normal, true, null), Options);

        var explanation = VerdictReasons.Explain(verdict);

        verdict.Word.Should().Be(VerdictWord.NotScored);
        explanation.Reasons.Should().ContainSingle();
        explanation.Reasons[0].Status.Should().Be(VerdictReasonStatus.NotApplicable);
        explanation.Reasons[0].Text.Should().Contain("Not a single equity");
        explanation.TurnsBuyWhen.Should().BeNull();
    }

    [Fact]
    public void Explain_ReadsTheBarsFromOptions_NotFromConstants()
    {
        var strict = new VerdictOptions { RulesVersion = 2, BuyStructureAtLeast = 80, BuyFundamentalsAtLeast = 80 };
        var verdict = VerdictRules.Evaluate(new VerdictInputs(60, 60, CrowdingClass.Normal, false, null), strict);

        var explanation = VerdictReasons.Explain(verdict);

        explanation.Reasons[0].Text.Should().Be("Structure 60 is below the BUY bar of 80.");
        explanation.TurnsBuyWhen.Should().Be("structure reaches 80 (reads 60) and fundamentals reach 80 (reads 60)");
    }

    [Fact]
    public void Explain_IsDeterministic()
    {
        var verdict = VerdictRules.Evaluate(new VerdictInputs(39, 78, CrowdingClass.Early, false, null), Options);

        VerdictReasons.Explain(verdict).Should().BeEquivalentTo(VerdictReasons.Explain(verdict));
    }

    [Theory]
    [InlineData(null, null, null, null)]
    [InlineData(0, 0, CrowdingClass.Extended, 50.0)]
    [InlineData(100, 100, CrowdingClass.Early, -80.0)]
    [InlineData(49, 49, CrowdingClass.Normal, 0.0)]
    [InlineData(50, 50, CrowdingClass.Normal, 10.0)]
    public void Explain_NeverThrows_AndKeepsWithinThreeToFiveReasonsForAScoredName(
        int? structure, int? fundamentals, CrowdingClass? crowding, double? premium)
    {
        var verdict = VerdictRules.Evaluate(
            new VerdictInputs(structure, fundamentals, crowding, false, premium is null ? null : (decimal)premium.Value), Options);

        var explanation = VerdictReasons.Explain(verdict);

        explanation.Reasons.Count.Should().BeInRange(3, 5);
        explanation.Reasons.Should().OnlyContain(r => r.Text.Length > 0);
    }

    private static VerdictExplanation Explain(int? structure, int? fundamentals, CrowdingClass? crowding, double? premium) =>
        VerdictReasons.Explain(VerdictRules.Evaluate(
            new VerdictInputs(structure, fundamentals, crowding, false, premium is null ? null : (decimal)premium.Value), Options));
}
