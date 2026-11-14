namespace FinanceSentry.Modules.Research.Tests.Scorecards;

using System.Globalization;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FluentAssertions;
using Xunit;

/// <summary>
/// Spec 726 US2 verdict calibration: the v2 rule table as a fixture. Each row is a scorecard and the rule
/// it must fire, so a threshold or order change that moves a word fails here and has to bump
/// <see cref="VerdictOptions.RulesVersion"/> on purpose.
/// </summary>
public sealed class VerdictRulesTests
{
    private static readonly VerdictOptions Options = new();

    // name, structure, fundamentals, crowding, valuation not applicable, price-lens premium %, revenue y/y %, lens, rule, word
    public static TheoryData<string, int?, int?, CrowdingClass?, bool, double?, double?, string?, string, VerdictWord> Calibration => new()
    {
        { "crypto", null, null, null, true, null, null, null, VerdictRules.OutsideUniverse, VerdictWord.NotScored },
        { "crypto with structure", 85, null, CrowdingClass.Normal, true, null, null, null, VerdictRules.OutsideUniverse, VerdictWord.NotScored },
        { "nothing read", null, null, null, false, null, null, null, VerdictRules.NoAxisData, VerdictWord.NotScored },
        { "broken chart", 22, 90, CrowdingClass.Normal, false, -5.0, 20.0, PriceLenses.OwnHistory, VerdictRules.WeakStructure, VerdictWord.Avoid },
        { "structure 29", 29, null, null, false, null, null, null, VerdictRules.WeakStructure, VerdictWord.Avoid },
        { "chased and rich", 88, 80, CrowdingClass.Extended, false, 29.2, 20.0, PriceLenses.OwnHistory, VerdictRules.ExtendedAndRich, VerdictWord.Avoid },
        { "chased and dearer than peers", 88, 80, CrowdingClass.Extended, false, 18.0, 20.0, PriceLenses.Peers, VerdictRules.ExtendedAndRich, VerdictWord.Avoid },
        { "fundamentals only", null, 75, null, false, null, null, null, VerdictRules.NotEnoughData, VerdictWord.Wait },
        { "valuation only", null, null, null, false, 4.0, null, PriceLenses.OwnHistory, VerdictRules.NotEnoughData, VerdictWord.Wait },
        { "clean leader", 78, 72, CrowdingClass.Normal, false, 8.5, 15.0, PriceLenses.OwnHistory, VerdictRules.StrongBuy, VerdictWord.StrongBuy },
        { "boundaries", 70, 70, CrowdingClass.Early, false, 10.0, 10.0, PriceLenses.OwnHistory, VerdictRules.StrongBuy, VerdictWord.StrongBuy },
        { "leader cheap against peers", 78, 72, CrowdingClass.Normal, false, -12.0, 15.0, PriceLenses.Peers, VerdictRules.StrongBuy, VerdictWord.StrongBuy },
        { "leader slightly rich", 78, 72, CrowdingClass.Normal, false, 10.1, 15.0, PriceLenses.OwnHistory, VerdictRules.Buy, VerdictWord.Buy },
        { "leader dearer than peers", 78, 72, CrowdingClass.Normal, false, 24.0, 15.0, PriceLenses.Peers, VerdictRules.Buy, VerdictWord.Buy },
        { "leader growing too slowly", 78, 72, CrowdingClass.Normal, false, 4.0, 9.9, PriceLenses.OwnHistory, VerdictRules.Buy, VerdictWord.Buy },
        { "leader shrinking", 78, 72, CrowdingClass.Normal, false, 4.0, -3.0, PriceLenses.OwnHistory, VerdictRules.Buy, VerdictWord.Buy },
        { "leader growth unread", 78, 72, CrowdingClass.Normal, false, 4.0, null, PriceLenses.OwnHistory, VerdictRules.Buy, VerdictWord.Buy },
        { "leader extended but cheap", 78, 72, CrowdingClass.Extended, false, -3.0, 15.0, PriceLenses.OwnHistory, VerdictRules.Buy, VerdictWord.Buy },
        { "leader no valuation", 78, 72, CrowdingClass.Normal, false, null, 15.0, null, VerdictRules.Buy, VerdictWord.Buy },
        { "leader no crowding", 78, 72, null, false, 2.0, 15.0, PriceLenses.OwnHistory, VerdictRules.Buy, VerdictWord.Buy },
        { "solid", 55, 60, CrowdingClass.Normal, false, 15.0, 12.0, PriceLenses.OwnHistory, VerdictRules.Buy, VerdictWord.Buy },
        { "structure only", 65, null, null, false, null, null, null, VerdictRules.BuyFundamentalsMissing, VerdictWord.Buy },
        { "strong chart no fundamentals", 90, null, CrowdingClass.Normal, false, 0.0, null, PriceLenses.OwnHistory, VerdictRules.BuyFundamentalsMissing, VerdictWord.Buy },
        { "strong business weak trend", 45, 80, CrowdingClass.Normal, false, -10.0, 20.0, PriceLenses.OwnHistory, VerdictRules.StrongBusinessWeakTrend, VerdictWord.Wait },
        { "strong business at the structure floor", 30, 90, null, false, null, null, null, VerdictRules.StrongBusinessWeakTrend, VerdictWord.Wait },
        { "trend without the business", 75, 40, CrowdingClass.Normal, false, 0.0, 5.0, PriceLenses.OwnHistory, VerdictRules.TrendWithoutBusiness, VerdictWord.Wait },
        { "weak trend, business unread", 40, null, null, false, null, null, null, VerdictRules.WeakTrendBusinessUnread, VerdictWord.Wait },
        { "both short of the bar", 45, 35, CrowdingClass.Normal, false, null, null, null, VerdictRules.Otherwise, VerdictWord.Wait },
        { "middling business, weak trend", 45, 60, CrowdingClass.Normal, false, null, null, null, VerdictRules.Otherwise, VerdictWord.Wait },
    };

    [Theory]
    [MemberData(nameof(Calibration))]
    public void Calibration_FiresTheExpectedRule(
        string name, int? structure, int? fundamentals, CrowdingClass? crowding, bool notApplicable, double? premium,
        double? revenueYoy, string? lens, string expectedRule, VerdictWord expectedWord)
    {
        var verdict = VerdictRules.Evaluate(Inputs(structure, fundamentals, crowding, notApplicable, premium, revenueYoy, lens), Options);

        verdict.FiredRuleId.Should().Be(expectedRule, name);
        verdict.Word.Should().Be(expectedWord, name);
        verdict.RulesVersion.Should().Be(Options.RulesVersion);
    }

    [Theory]
    [MemberData(nameof(Calibration))]
    public void Calibration_WordIsReDerivableFromThePublishedConditions(
        string name, int? structure, int? fundamentals, CrowdingClass? crowding, bool notApplicable, double? premium,
        double? revenueYoy, string? lens, string expectedRule, VerdictWord expectedWord)
    {
        _ = expectedRule;
        _ = expectedWord;
        var verdict = VerdictRules.Evaluate(Inputs(structure, fundamentals, crowding, notApplicable, premium, revenueYoy, lens), Options);

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
            .Select(row => (string)row[8])
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
            VerdictRules.NotEnoughData,
            VerdictRules.StrongBuy,
            VerdictRules.Buy,
            VerdictRules.BuyFundamentalsMissing,
            VerdictRules.StrongBusinessWeakTrend,
            VerdictRules.TrendWithoutBusiness,
            VerdictRules.WeakTrendBusinessUnread,
            VerdictRules.Otherwise);
        verdict.Rules.Single(r => r.Id == VerdictRules.Otherwise).Conditions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(45, 55, "adjusted structure 45 is below the BUY bar of 50")]
    [InlineData(45, 35, "adjusted structure 45 is below the BUY bar of 50 and fundamentals 35 are below the BUY bar of 50")]
    public void Evaluate_Otherwise_NamesTheBarsMissedWithTheirValues(int structure, int fundamentals, string expected)
    {
        var verdict = VerdictRules.Evaluate(Inputs(structure, fundamentals, CrowdingClass.Normal, false, null), Options);

        verdict.FiredRuleId.Should().Be(VerdictRules.Otherwise);
        verdict.Word.Should().Be(VerdictWord.Wait);
        verdict.Rules.Single(r => r.Id == VerdictRules.Otherwise).Description.Should().Contain(expected);
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
        var verdict = VerdictRules.Evaluate(Inputs(78, null, null, false, null, null), Options);

        var strongBuy = verdict.Rules.Single(r => r.Id == VerdictRules.StrongBuy);
        strongBuy.Matched.Should().BeFalse();
        strongBuy.Conditions.Where(c => c.Axis != VerdictAxis.Structure)
            .Should().OnlyContain(c => c.Actual == null && !c.Met);
    }

    [Fact]
    public void ThresholdsComeFromOptions()
    {
        var strict = new VerdictOptions { RulesVersion = 3, BuyStructureAtLeast = 80, BuyFundamentalsAtLeast = 80 };

        var verdict = VerdictRules.Evaluate(Inputs(55, 60, CrowdingClass.Normal, false, 15.0), strict);

        verdict.Word.Should().Be(VerdictWord.Wait);
        verdict.RulesVersion.Should().Be(3);
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

    [Fact]
    public void RulesVersion_IsTwo()
    {
        Options.RulesVersion.Should().Be(2);
    }

    [Theory]
    [MemberData(nameof(Calibration))]
    public void EveryWait_NamesItsReason_AndNoOtherWordDoes(
        string name, int? structure, int? fundamentals, CrowdingClass? crowding, bool notApplicable, double? premium,
        double? revenueYoy, string? lens, string expectedRule, VerdictWord expectedWord)
    {
        _ = expectedRule;
        var verdict = VerdictRules.Evaluate(Inputs(structure, fundamentals, crowding, notApplicable, premium, revenueYoy, lens), Options);

        if (expectedWord == VerdictWord.Wait)
        {
            verdict.WaitReason.Should().NotBeNullOrEmpty(name);
            WaitReasons.Label(verdict.WaitReason).Should().NotBeNullOrEmpty(name);
        }
        else
        {
            verdict.WaitReason.Should().BeNull(name);
        }
    }

    [Theory]
    [InlineData(VerdictRules.NotEnoughData, WaitReasons.NotEnoughData, "Not enough data")]
    [InlineData(VerdictRules.StrongBusinessWeakTrend, WaitReasons.StrongBusinessWeakTrend, "Strong business, weak trend")]
    [InlineData(VerdictRules.TrendWithoutBusiness, WaitReasons.TrendWithoutBusiness, "Trend without the business")]
    [InlineData(VerdictRules.WeakTrendBusinessUnread, WaitReasons.NotEnoughData, "Not enough data")]
    [InlineData(VerdictRules.Otherwise, WaitReasons.ShortOfBuyBar, "Short of the BUY bar")]
    public void NamedWaits_MapRuleToReasonAndLabel(string ruleId, string reason, string label)
    {
        var fired = Calibration
            .Where(row => (string)row[8] == ruleId)
            .Select(row => VerdictRules.Evaluate(
                Inputs((int?)row[1], (int?)row[2], (CrowdingClass?)row[3], (bool)row[4], (double?)row[5], (double?)row[6], (string?)row[7]),
                Options))
            .First();

        fired.WaitReason.Should().Be(reason);
        WaitReasons.Label(fired.WaitReason).Should().Be(label);
    }

    [Fact]
    public void WaitBoundaries_StrongBusinessNeedsSeventy_TrendWithoutBusinessNeedsFiftyOfStructure()
    {
        VerdictRules.Evaluate(Inputs(49, 70, CrowdingClass.Normal, false, null), Options).FiredRuleId
            .Should().Be(VerdictRules.StrongBusinessWeakTrend);
        VerdictRules.Evaluate(Inputs(49, 69, CrowdingClass.Normal, false, null), Options).FiredRuleId
            .Should().Be(VerdictRules.Otherwise);
        VerdictRules.Evaluate(Inputs(50, 49, CrowdingClass.Normal, false, null), Options).FiredRuleId
            .Should().Be(VerdictRules.TrendWithoutBusiness);
        VerdictRules.Evaluate(Inputs(50, 50, CrowdingClass.Normal, false, null), Options).FiredRuleId
            .Should().Be(VerdictRules.Buy);
    }

    [Theory]
    [InlineData(10.0, VerdictWord.StrongBuy)]
    [InlineData(9.9, VerdictWord.Buy)]
    [InlineData(null, VerdictWord.Buy)]
    public void GrowthGate_StrongBuyNeedsRevenueGrowthOfTenPercent(double? revenueYoy, VerdictWord expected)
    {
        var verdict = VerdictRules.Evaluate(Inputs(78, 72, CrowdingClass.Normal, false, 4.0, revenueYoy), Options);

        verdict.Word.Should().Be(expected);
        var growth = verdict.Rules.Single(r => r.Id == VerdictRules.StrongBuy).Conditions.Single(c => c.Axis == VerdictAxis.Growth);
        growth.Threshold.Should().Be("10");
        growth.Actual.Should().Be(revenueYoy?.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void PriceLens_IsCarriedOnTheVerdict_AndReadByTheValuationConditions()
    {
        var peers = VerdictRules.Evaluate(Inputs(78, 72, CrowdingClass.Normal, false, 6.0, 15.0, PriceLenses.Peers), Options);
        var none = VerdictRules.Evaluate(Inputs(78, 72, CrowdingClass.Normal, false, null), Options);

        peers.PriceLens.Should().Be(PriceLenses.Peers);
        peers.Word.Should().Be(VerdictWord.StrongBuy);
        none.PriceLens.Should().BeNull();
        none.Word.Should().Be(VerdictWord.Buy);
    }

    /// <summary>
    /// Release calibration: v2 is v1 plus three changes, so over a grid of axis readings the v2 word equals the v1
    /// word except where a documented change applies. The v1 table is re-stated here as the reference.
    /// </summary>
    [Fact]
    public void Calibration_V2DiffersFromV1OnlyWhereAChangeApplies()
    {
        var structures = new int?[] { null, 20, 30, 45, 50, 69, 70, 90 };
        var fundamentals = new int?[] { null, 30, 50, 69, 70, 95 };
        var crowdings = new CrowdingClass?[] { null, CrowdingClass.Early, CrowdingClass.Extended };
        var premiums = new double?[] { null, -20.0, 0.0, 10.0, 10.1, 40.0 };
        var growths = new double?[] { null, -5.0, 9.9, 10.0, 30.0 };
        var downgrades = 0;

        foreach (var structure in structures)
        foreach (var fundamental in fundamentals)
        foreach (var crowding in crowdings)
        foreach (var premium in premiums)
        foreach (var growth in growths)
        {
            var inputs = Inputs(structure, fundamental, crowding, false, premium, growth);
            var v2 = VerdictRules.Evaluate(inputs, Options).Word;
            var v1 = LegacyV1Word(structure, fundamental, crowding, premium);

            if (v2 == v1)
            {
                continue;
            }

            // The only own-history difference is the growth gate: STRONG BUY without the growth reads BUY.
            v1.Should().Be(VerdictWord.StrongBuy, $"{structure}/{fundamental}/{crowding}/{premium}/{growth}");
            v2.Should().Be(VerdictWord.Buy);
            (growth is null || growth < 10.0).Should().BeTrue();
            downgrades++;
        }

        downgrades.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Calibration_NoV2WaitOrAvoidWordMovesWhenThePeerLensIsAbsent()
    {
        var structures = new int?[] { null, 20, 40, 60, 80 };
        var fundamentals = new int?[] { null, 30, 60, 80 };

        foreach (var structure in structures)
        foreach (var fundamental in fundamentals)
        foreach (var crowding in new CrowdingClass?[] { null, CrowdingClass.Normal, CrowdingClass.Extended })
        {
            var word = VerdictRules.Evaluate(Inputs(structure, fundamental, crowding, false, null, 30.0), Options).Word;
            var v1 = LegacyV1Word(structure, fundamental, crowding, null);

            word.Should().Be(v1 == VerdictWord.StrongBuy ? VerdictWord.Buy : v1, $"{structure}/{fundamental}/{crowding}");
        }
    }

    private static VerdictWord LegacyV1Word(int? structure, int? fundamentals, CrowdingClass? crowding, double? premium)
    {
        if (structure is null && fundamentals is null && crowding is null && premium is null)
        {
            return VerdictWord.NotScored;
        }

        if (structure < 30)
        {
            return VerdictWord.Avoid;
        }

        if (crowding == CrowdingClass.Extended && premium > 0)
        {
            return VerdictWord.Avoid;
        }

        if (structure is null)
        {
            return VerdictWord.Wait;
        }

        if (structure >= 70 && fundamentals >= 70 && crowding is not null && crowding != CrowdingClass.Extended && premium <= 10)
        {
            return VerdictWord.StrongBuy;
        }

        if (structure >= 50 && fundamentals >= 50)
        {
            return VerdictWord.Buy;
        }

        return structure >= 50 && fundamentals is null ? VerdictWord.Buy : VerdictWord.Wait;
    }

    private static VerdictInputs Inputs(
        int? structure, int? fundamentals, CrowdingClass? crowding, bool notApplicable, double? premium,
        double? revenueYoy = 15.0, string? lens = null) =>
        new(
            structure,
            fundamentals,
            crowding,
            notApplicable,
            premium is null ? null : (decimal)premium.Value,
            revenueYoy is null ? null : (decimal)revenueYoy.Value,
            lens ?? (premium is null ? null : PriceLenses.OwnHistory));

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
