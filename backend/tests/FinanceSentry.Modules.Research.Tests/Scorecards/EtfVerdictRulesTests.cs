namespace FinanceSentry.Modules.Research.Tests.Scorecards;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FluentAssertions;
using Xunit;

/// <summary>
/// Issue #1324: the small published ETF table as a fixture. Each row is a fund read and the rule it must fire, so a
/// threshold or order change that moves a word fails here and has to bump <see cref="VerdictOptions.EtfRulesVersion"/>.
/// </summary>
public sealed class EtfVerdictRulesTests
{
    private static readonly VerdictOptions Options = new();

    public static TheoryData<string, int?, CrowdingClass?, double?, string, VerdictWord> Calibration => new()
    {
        { "nothing read", null, null, null, EtfVerdictRules.NoData, VerdictWord.NotScored },
        { "broken fund chart", 29, CrowdingClass.Normal, 90.0, EtfVerdictRules.WeakStructure, VerdictWord.Avoid },
        { "broken chart, holdings missing", 12, null, null, EtfVerdictRules.WeakStructure, VerdictWord.Avoid },
        { "holdings only", null, null, 80.0, EtfVerdictRules.StructureMissing, VerdictWord.Wait },
        { "clean fund", 60, CrowdingClass.Normal, 70.0, EtfVerdictRules.Buy, VerdictWord.Buy },
        { "boundaries", 50, CrowdingClass.Early, 50.1, EtfVerdictRules.Buy, VerdictWord.Buy },
        { "capped at BUY", 99, CrowdingClass.Normal, 100.0, EtfVerdictRules.Buy, VerdictWord.Buy },
        { "half the weight is not most", 70, CrowdingClass.Normal, 50.0, EtfVerdictRules.Otherwise, VerdictWord.Wait },
        { "extended fund", 70, CrowdingClass.Extended, 90.0, EtfVerdictRules.Otherwise, VerdictWord.Wait },
        { "no crowding read", 70, null, 90.0, EtfVerdictRules.Otherwise, VerdictWord.Wait },
        { "middling chart", 49, CrowdingClass.Normal, 90.0, EtfVerdictRules.Otherwise, VerdictWord.Wait },
        { "structure 30 is not AVOID", 30, CrowdingClass.Normal, 90.0, EtfVerdictRules.Otherwise, VerdictWord.Wait },
        { "holdings missing", 80, CrowdingClass.Normal, null, EtfVerdictRules.Otherwise, VerdictWord.Wait },
    };

    [Theory]
    [MemberData(nameof(Calibration))]
    public void Calibration_FiresTheExpectedRule(
        string name, int? structure, CrowdingClass? crowding, double? share, string expectedRule, VerdictWord expectedWord)
    {
        var verdict = EtfVerdictRules.Evaluate(Inputs(structure, crowding, share), Options);

        verdict.FiredRuleId.Should().Be(expectedRule, name);
        verdict.Word.Should().Be(expectedWord, name);
        verdict.RulesVersion.Should().Be(Options.EtfRulesVersion);
        EtfVerdictRules.Reads(verdict).Should().BeTrue();
    }

    [Fact]
    public void EveryRuleOfTheTable_FiresForAtLeastOneCalibrationRow()
    {
        var fired = Calibration.Select(row => (string)row[4]).ToHashSet();

        var all = EtfVerdictRules.Evaluate(Inputs(null, null, null), Options).Rules.Select(r => r.Id);
        fired.Should().BeEquivalentTo(all);
    }

    [Fact]
    public void Evaluate_ReturnsTheWholeTableInOrder()
    {
        var verdict = EtfVerdictRules.Evaluate(Inputs(60, CrowdingClass.Normal, 70.0), Options);

        verdict.Rules.Select(r => r.Id).Should().Equal(
            EtfVerdictRules.NoData,
            EtfVerdictRules.WeakStructure,
            EtfVerdictRules.StructureMissing,
            EtfVerdictRules.Buy,
            EtfVerdictRules.Otherwise);
    }

    [Fact]
    public void NoRuleOfTheTable_ReadsStrongBuy()
    {
        var verdict = EtfVerdictRules.Evaluate(Inputs(100, CrowdingClass.Early, 100.0), Options);

        verdict.Rules.Select(r => r.Word).Should().NotContain(VerdictWord.StrongBuy);
    }

    [Fact]
    public void MissingLookThrough_IsPublishedAsNullActual_AndNeverMeetsTheBuyBar()
    {
        var verdict = EtfVerdictRules.Evaluate(Inputs(80, CrowdingClass.Normal, null), Options);

        var buy = verdict.Rules.Single(r => r.Id == EtfVerdictRules.Buy);
        buy.Matched.Should().BeFalse();
        var lookThrough = buy.Conditions.Single(c => c.Axis == VerdictAxis.LookThrough);
        lookThrough.Actual.Should().BeNull();
        lookThrough.Met.Should().BeFalse();
        verdict.Rules.Single(r => r.Id == EtfVerdictRules.Otherwise).Description
            .Should().Contain("top holdings could not be read, so the look-through was not applied");
    }

    [Fact]
    public void Otherwise_NamesTheBarsMissedWithTheirValues()
    {
        var verdict = EtfVerdictRules.Evaluate(Inputs(45, CrowdingClass.Extended, 40.0), Options);

        verdict.Rules.Single(r => r.Id == EtfVerdictRules.Otherwise).Description.Should().Contain(
            "the fund's adjusted structure 45 is below the BUY bar of 50 and the fund's crowding is Extended "
            + "and only 40% of its top-10 weight reads BUY or better (more than 50% is needed)");
    }

    [Fact]
    public void Evaluate_ReadsTheThresholdsFromTheOptions()
    {
        var strict = new VerdictOptions { BuyStructureAtLeast = 70, EtfBuyWeightSharePctAbove = 80m, AvoidStructureBelow = 40 };

        EtfVerdictRules.Evaluate(Inputs(60, CrowdingClass.Normal, 90.0), strict).Word.Should().Be(VerdictWord.Wait);
        EtfVerdictRules.Evaluate(Inputs(75, CrowdingClass.Normal, 79.0), strict).Word.Should().Be(VerdictWord.Wait);
        EtfVerdictRules.Evaluate(Inputs(75, CrowdingClass.Normal, 81.0), strict).Word.Should().Be(VerdictWord.Buy);
        EtfVerdictRules.Evaluate(Inputs(35, CrowdingClass.Normal, 81.0), strict).Word.Should().Be(VerdictWord.Avoid);
    }

    [Fact]
    public void ASingleNameVerdict_IsNotReadAsAnEtfVerdict()
    {
        var verdict = VerdictRules.Evaluate(new VerdictInputs(78, 72, CrowdingClass.Normal, false, 8.5m), Options);

        EtfVerdictRules.Reads(verdict).Should().BeFalse();
    }

    private static EtfVerdictInputs Inputs(int? structure, CrowdingClass? crowding, double? share) =>
        new(structure, crowding, (decimal?)share);
}

public sealed class LookThroughWeightsTests
{
    [Fact]
    public void Share_IsTheBuyOrBetterWeightOverTheWholeListedWeight()
    {
        WeightedWord[] holdings =
        [
            new(40m, VerdictWord.Buy),
            new(20m, VerdictWord.StrongBuy),
            new(30m, VerdictWord.Wait),
            new(10m, VerdictWord.Avoid),
        ];

        LookThroughWeights.Covered(holdings).Should().Be(100m);
        LookThroughWeights.BuyWeight(holdings).Should().Be(60m);
        LookThroughWeights.BuyWeightSharePct(holdings).Should().Be(60m);
    }

    [Fact]
    public void AHoldingThatCouldNotBeScored_CountsAgainstTheFund()
    {
        WeightedWord[] holdings = [new(30m, VerdictWord.Buy), new(30m, null), new(40m, VerdictWord.NotScored)];

        LookThroughWeights.BuyWeightSharePct(holdings).Should().Be(30m);
    }

    [Fact]
    public void Share_IsRoundedToOneDecimal()
    {
        WeightedWord[] holdings = [new(1m, VerdictWord.Buy), new(2m, VerdictWord.Wait)];

        LookThroughWeights.BuyWeightSharePct(holdings).Should().Be(33.3m);
    }

    [Fact]
    public void NoHoldings_HaveNoShare()
    {
        LookThroughWeights.BuyWeightSharePct([]).Should().BeNull();
    }
}
