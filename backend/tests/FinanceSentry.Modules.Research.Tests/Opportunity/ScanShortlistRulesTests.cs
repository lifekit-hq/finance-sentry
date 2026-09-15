namespace FinanceSentry.Modules.Research.Tests.Opportunity;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FluentAssertions;
using Xunit;

/// <summary>
/// Stage 1 of the #558 funnel: the composition rules that turn an index-sized list plus two cheap
/// signals into the bounded shortlist stage 2 pays bar math for.
/// </summary>
public sealed class ScanShortlistRulesTests
{
    private static readonly OpportunityOptions Options = new();

    private static readonly string[] TenNames =
        [.. Enumerable.Range(0, 10).Select(i => FormattableString.Invariant($"T{i:D2}"))];

    [Fact]
    public void Slate_RanksTheIndexOnPercentChange_AndStopsAtTheGradingBudget()
    {
        var options = new OpportunityOptions { ScanShortlistGradeBudget = 3, ScanShortlistStreetWeight = 0m };

        var slate = ScanShortlistRules.SelectGradingSlate(TenNames, RisingPercentChange(), NoActions(), options);

        slate.Select(e => e.Ticker).Should().Equal("T09", "T08", "T07");
        slate[0].MomentumPercentile.Should().Be(100m, "the fastest riser tops an inclusive percentile rank");
    }

    [Fact]
    public void Slate_LetsTheStreetCarryAName_TheDaysMoveDoesNot()
    {
        // T00 is the worst mover in the index but the only one the street touched three times.
        var slate = ScanShortlistRules.SelectGradingSlate(
            TenNames,
            RisingPercentChange(),
            new Dictionary<string, int> { ["T00"] = 3 },
            new OpportunityOptions { ScanShortlistGradeBudget = 2, ScanShortlistStreetWeight = 0.5m });

        slate.Should().Contain(e => e.Ticker == "T00");
        slate.Single(e => e.Ticker == "T00").FavourableActions.Should().Be(3);
    }

    [Fact]
    public void Slate_CapsTheStreetSignal_SoOneNoisyNameCannotOwnTheSlate()
    {
        var options = new OpportunityOptions { ScanShortlistStreetWeight = 1m, ScanShortlistStreetActionPoints = 50 };

        var slate = ScanShortlistRules.SelectGradingSlate(
            TenNames, RisingPercentChange(), new Dictionary<string, int> { ["T00"] = 2, ["T01"] = 40 }, options);

        slate.Select(e => e.SurfaceScore).Should().OnlyContain(score => score <= 100m);
        slate.Take(2).Select(e => e.Ticker).Should().BeEquivalentTo(new[] { "T00", "T01" });
    }

    [Fact]
    public void Slate_SkipsANameWithNeitherAQuoteNorAnAction()
    {
        var slate = ScanShortlistRules.SelectGradingSlate(
            TenNames,
            new Dictionary<string, decimal> { ["T03"] = 1.5m },
            NoActions(),
            Options);

        slate.Should().ContainSingle().Which.Ticker.Should().Be("T03");
    }

    [Fact]
    public void Slate_NormalisesAndDeduplicatesTheIndex()
    {
        var slate = ScanShortlistRules.SelectGradingSlate(
            [" t03 ", "T03", ""],
            new Dictionary<string, decimal> { ["T03"] = 1.5m },
            NoActions(),
            Options);

        slate.Should().ContainSingle().Which.Ticker.Should().Be("T03");
    }

    [Fact]
    public void Compose_RanksTheSlateOnQuality_AndCapsItAtTheShortlistSize()
    {
        var options = new OpportunityOptions
        {
            ScanShortlistGradeBudget = 10,
            ScanShortlistSize = 2,
            ScanShortlistStreetWeight = 0m,
            ScanShortlistQualityWeight = 1m,
        };
        var slate = ScanShortlistRules.SelectGradingSlate(TenNames, RisingPercentChange(), NoActions(), options);

        var shortlist = ScanShortlistRules.Compose(
            slate,
            TenNames.ToDictionary(t => t, t => (int?)(t == "T02" ? 95 : t == "T03" ? 90 : 10)),
            options);

        shortlist.Select(e => e.Ticker).Should().Equal(
            ["T02", "T03"], "quality decides the shortlist once the cheap signals have bounded the slate");
        shortlist[0].ShortlistScore.Should().Be(95m);
    }

    [Fact]
    public void Compose_KeepsAnUngradedNameBelowEveryGradedOne_RatherThanFakingItsHalf()
    {
        var options = new OpportunityOptions { ScanShortlistStreetWeight = 0m, ScanShortlistSize = 10 };
        var slate = ScanShortlistRules.SelectGradingSlate(TenNames, RisingPercentChange(), NoActions(), options);

        // T09 is the strongest mover, but EDGAR has no answer for it; T00 is the weakest and grades 1.
        var shortlist = ScanShortlistRules.Compose(
            slate,
            new Dictionary<string, int?> { ["T09"] = null, ["T00"] = 1 },
            options);

        shortlist[0].Ticker.Should().Be("T00", "even a weak grade outranks a name EDGAR could not grade");
        shortlist[0].ShortlistScore.Should().NotBeNull();

        var strongestMover = shortlist[1];
        strongestMover.Ticker.Should().Be("T09", "ungraded names follow the graded ones in surface order");
        strongestMover.ShortlistScore.Should().BeNull("a missing grade is never defaulted to a number");
        strongestMover.SurfaceScore.Should().Be(100m, "the ungraded name keeps its surface standing");
    }

    [Fact]
    public void Compose_ClampsAMisconfiguredQualityWeight_ToPureQuality()
    {
        var options = new OpportunityOptions { ScanShortlistQualityWeight = 4m, ScanShortlistStreetWeight = 0m };
        var slate = ScanShortlistRules.SelectGradingSlate(TenNames, RisingPercentChange(), NoActions(), options);

        var shortlist = ScanShortlistRules.Compose(
            slate, TenNames.ToDictionary(t => t, t => (int?)(t == "T00" ? 80 : 20)), options);

        shortlist[0].Ticker.Should().Be("T00");
        shortlist[0].ShortlistScore.Should().Be(80m);
    }

    [Fact]
    public void Compose_OfAnEmptySlate_IsEmpty()
        => ScanShortlistRules.Compose([], new Dictionary<string, int?>(), Options).Should().BeEmpty();

    /// <summary>T00..T09 rising 0.0% to 0.9% on the day — a deterministic momentum ordering.</summary>
    private static Dictionary<string, decimal> RisingPercentChange()
        => TenNames.Select((t, i) => (t, change: i * 0.1m)).ToDictionary(x => x.t, x => x.change);

    private static Dictionary<string, int> NoActions() => [];
}
