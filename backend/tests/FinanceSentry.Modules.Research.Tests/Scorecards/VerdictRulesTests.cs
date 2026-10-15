namespace FinanceSentry.Modules.Research.Tests.Scorecards;

using System.Globalization;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FluentAssertions;
using Xunit;

/// <summary>
/// Spec 726 US2 verdict calibration: the v1 rule table as a fixture. Each row is a scorecard and the rule
/// it must fire, so a threshold or order change that moves a word fails here and has to bump
/// <see cref="VerdictOptions.RulesVersion"/> on purpose.
/// </summary>
public sealed class VerdictRulesTests
{
    private static readonly VerdictOptions Options = new();

    public static TheoryData<string, int?, int?, CrowdingClass?, bool, double?, string, VerdictWord> Calibration => new()
    {
        { "crypto", null, null, null, true, null, VerdictRules.OutsideUniverse, VerdictWord.NotScored },
        { "crypto with structure", 85, null, CrowdingClass.Normal, true, null, VerdictRules.OutsideUniverse, VerdictWord.NotScored },
        { "nothing read", null, null, null, false, null, VerdictRules.NoAxisData, VerdictWord.NotScored },
        { "broken chart", 22, 90, CrowdingClass.Normal, false, -5.0, VerdictRules.WeakStructure, VerdictWord.Avoid },
        { "structure 29", 29, null, null, false, null, VerdictRules.WeakStructure, VerdictWord.Avoid },
        { "chased and rich", 88, 80, CrowdingClass.Extended, false, 29.2, VerdictRules.ExtendedAndRich, VerdictWord.Avoid },
        { "fundamentals only", null, 75, null, false, null, VerdictRules.StructureMissing, VerdictWord.Wait },
        { "valuation only", null, null, null, false, 4.0, VerdictRules.StructureMissing, VerdictWord.Wait },
        { "clean leader", 78, 72, CrowdingClass.Normal, false, 8.5, VerdictRules.StrongBuy, VerdictWord.StrongBuy },
        { "boundaries", 70, 70, CrowdingClass.Early, false, 10.0, VerdictRules.StrongBuy, VerdictWord.StrongBuy },
        { "leader slightly rich", 78, 72, CrowdingClass.Normal, false, 10.1, VerdictRules.Buy, VerdictWord.Buy },
        { "leader extended but cheap", 78, 72, CrowdingClass.Extended, false, -3.0, VerdictRules.Buy, VerdictWord.Buy },
        { "leader no valuation", 78, 72, CrowdingClass.Normal, false, null, VerdictRules.Buy, VerdictWord.Buy },
        { "leader no crowding", 78, 72, null, false, 2.0, VerdictRules.Buy, VerdictWord.Buy },
        { "solid", 55, 60, CrowdingClass.Normal, false, 15.0, VerdictRules.Buy, VerdictWord.Buy },
        { "structure only", 65, null, null, false, null, VerdictRules.BuyFundamentalsMissing, VerdictWord.Buy },
        { "strong chart no fundamentals", 90, null, CrowdingClass.Normal, false, 0.0, VerdictRules.BuyFundamentalsMissing, VerdictWord.Buy },
        { "weak fundamentals", 75, 40, CrowdingClass.Normal, false, 0.0, VerdictRules.Otherwise, VerdictWord.Wait },
        { "middling chart", 45, 80, CrowdingClass.Normal, false, -10.0, VerdictRules.Otherwise, VerdictWord.Wait },
        { "structure 30", 30, 90, null, false, null, VerdictRules.Otherwise, VerdictWord.Wait },
    };

    [Theory]
    [MemberData(nameof(Calibration))]
    public void Calibration_FiresTheExpectedRule(
        string name, int? structure, int? fundamentals, CrowdingClass? crowding, bool notApplicable, double? premium,
        string expectedRule, VerdictWord expectedWord)
    {
        var verdict = VerdictRules.Evaluate(Inputs(structure, fundamentals, crowding, notApplicable, premium), Options);

        verdict.FiredRuleId.Should().Be(expectedRule, name);
        verdict.Word.Should().Be(expectedWord, name);
        verdict.RulesVersion.Should().Be(Options.RulesVersion);
    }

    [Theory]
    [MemberData(nameof(Calibration))]
    public void Calibration_WordIsReDerivableFromThePublishedConditions(
        string name, int? structure, int? fundamentals, CrowdingClass? crowding, bool notApplicable, double? premium,
        string expectedRule, VerdictWord expectedWord)
    {
        _ = expectedRule;
        _ = expectedWord;
        var verdict = VerdictRules.Evaluate(Inputs(structure, fundamentals, crowding, notApplicable, premium), Options);

        // SC-002: a reader with only the published table (operator, threshold, actual) reaches the same word.
        var reDerived = verdict.Rules.First(r => r.Conditions.All(ReCheck));
        reDerived.Id.Should().Be(verdict.FiredRuleId, name);
        reDerived.Word.Should().Be(verdict.Word, name);
        verdict.Rules.Should().OnlyContain(r => r.Matched == r.Conditions.All(ReCheck), name);
    }

    [Fact]
    public void EveryRuleOfTheTable_FiresForAtLeastOneCalibrationRow()
    {
        var fired = Calibration
            .Select(row => (string)row[6])
            .ToHashSet();

        var all = VerdictRules.Evaluate(Inputs(null, null, null, false, null), Options).Rules.Select(r => r.Id);
        fired.Should().BeEquivalentTo(all);
    }

    [Fact]
    public void Evaluate_ReturnsTheWholeTableInOrder()
    {
        var verdict = VerdictRules.Evaluate(Inputs(78, 72, CrowdingClass.Normal, false, 8.5), Options);

        verdict.Rules.Select(r => r.Id).Should().Equal(
            VerdictRules.OutsideUniverse,
            VerdictRules.NoAxisData,
            VerdictRules.WeakStructure,
            VerdictRules.ExtendedAndRich,
            VerdictRules.StructureMissing,
            VerdictRules.StrongBuy,
            VerdictRules.Buy,
            VerdictRules.BuyFundamentalsMissing,
            VerdictRules.Otherwise);
        verdict.Rules.Single(r => r.Id == VerdictRules.Otherwise).Conditions.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_IsDeterministic()
    {
        var inputs = Inputs(78, 72, CrowdingClass.Normal, false, 8.5);

        var first = VerdictRules.Evaluate(inputs, Options);
        var second = VerdictRules.Evaluate(inputs, Options);

        second.Should().BeEquivalentTo(first);
    }

    [Fact]
    public void MissingAxis_IsPublishedAsNullActual_AndNeverMeetsAComparison()
    {
        var verdict = VerdictRules.Evaluate(Inputs(78, null, null, false, null), Options);

        var strongBuy = verdict.Rules.Single(r => r.Id == VerdictRules.StrongBuy);
        strongBuy.Matched.Should().BeFalse();
        strongBuy.Conditions.Where(c => c.Axis != VerdictAxis.Structure)
            .Should().OnlyContain(c => c.Actual == null && !c.Met);
    }

    [Fact]
    public void ThresholdsComeFromOptions()
    {
        var strict = new VerdictOptions { RulesVersion = 2, BuyStructureAtLeast = 80, BuyFundamentalsAtLeast = 80 };

        var verdict = VerdictRules.Evaluate(Inputs(55, 60, CrowdingClass.Normal, false, 15.0), strict);

        verdict.Word.Should().Be(VerdictWord.Wait);
        verdict.RulesVersion.Should().Be(2);
        verdict.Rules.Single(r => r.Id == VerdictRules.Buy).Conditions[0].Threshold.Should().Be("80");
    }

    [Fact]
    public void Values_AreFormattedWithTheInvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("uk-UA");
            var verdict = VerdictRules.Evaluate(Inputs(78, 72, CrowdingClass.Normal, false, 8.5), Options);

            var premium = verdict.Rules.Single(r => r.Id == VerdictRules.StrongBuy)
                .Conditions.Single(c => c.Axis == VerdictAxis.Valuation);
            premium.Actual.Should().Be("8.5");
            premium.Threshold.Should().Be("10");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static VerdictInputs Inputs(
        int? structure, int? fundamentals, CrowdingClass? crowding, bool notApplicable, double? premium) =>
        new(structure, fundamentals, crowding, notApplicable, premium is null ? null : (decimal)premium.Value);

    /// <summary>Re-checks one condition from its published strings only, as a reader would.</summary>
    private static bool ReCheck(VerdictCondition c)
    {
        if (c.Operator == VerdictOperator.IsMissing)
        {
            return c.Actual is null;
        }

        if (c.Actual is null)
        {
            return false;
        }

        return c.Operator switch
        {
            VerdictOperator.Is => c.Actual == c.Threshold,
            VerdictOperator.IsNot => c.Actual != c.Threshold,
            _ => CompareNumbers(c.Operator, Parse(c.Actual), Parse(c.Threshold!)),
        };
    }

    private static bool CompareNumbers(VerdictOperator op, decimal actual, decimal threshold) => op switch
    {
        VerdictOperator.LessThan => actual < threshold,
        VerdictOperator.GreaterThan => actual > threshold,
        VerdictOperator.AtLeast => actual >= threshold,
        VerdictOperator.AtMost => actual <= threshold,
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, null),
    };

    private static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
