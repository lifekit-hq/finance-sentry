namespace FinanceSentry.Modules.Research.Tests.Opportunity;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FluentAssertions;
using Xunit;

public sealed class ScanNominationRulesTests
{
    private static readonly OpportunityOptions Options = new();

    private static UniverseStructureEntry Entry(
        string ticker,
        decimal? rs,
        bool isEtfLens = false,
        bool stale = false,
        int? sectorRank = null,
        decimal? distanceFrom63dHigh = -0.05m,
        decimal? volumeRatio = 1m)
        => new(
            ticker,
            isEtfLens,
            new MarketStructureSnapshot(
                ticker,
                new Dictionary<int, decimal?> { [ScanNominationRules.RsWindowBars] = rs },
                new Dictionary<int, decimal?>(),
                ExtensionFromMa50: 0.05m,
                TodayZScore: 0m,
                VolumeRatio: volumeRatio,
                Ma50: 50m,
                Ma200: 48m,
                Stale: stale,
                SectorRank: sectorRank,
                SectorRankDelta: null,
                DistanceFrom63dHigh: distanceFrom63dHigh));

    private static List<UniverseStructureEntry> TenTickerUniverse()
    {
        // RS 0.01..0.10 → percentiles 10..100. T10 is top-decile; T8-T10 top-quartile.
        var entries = new List<UniverseStructureEntry>();
        for (var i = 1; i <= 10; i++)
        {
            entries.Add(Entry($"T{i:00}", rs: 0.01m * i));
        }

        return entries;
    }

    [Fact]
    public void Evaluate_EmptyUniverse_ReturnsEmpty()
        => ScanNominationRules.Evaluate([], Options).Should().BeEmpty();

    [Fact]
    public void Evaluate_NothingQualifies_ReturnsEmpty_SilenceIsValid()
    {
        var universe = new List<UniverseStructureEntry>
        {
            Entry("AAA", rs: null),
            Entry("BBB", rs: null),
        };

        ScanNominationRules.Evaluate(universe, Options).Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_TopDecileRs_Nominated()
    {
        var nominations = ScanNominationRules.Evaluate(TenTickerUniverse(), Options);

        nominations.Should().Contain(n => n.Ticker == "T10")
            .Which.Reasons.Should().Contain(ScanNominationRules.TopDecileRsReason);
        nominations.Should().NotContain(n => n.Ticker == "T08");
    }

    [Fact]
    public void Evaluate_TopQuartileRsInTopRotatingSector_Nominated()
    {
        var universe = TenTickerUniverse();
        // T09 (90th percentile) sits in the #1 rotating sector → rule (a) fires; decile rule also fires.
        universe[8] = Entry("T09", rs: 0.09m, sectorRank: 1);

        var nominations = ScanNominationRules.Evaluate(universe, Options);

        nominations.Should().Contain(n => n.Ticker == "T09")
            .Which.Reasons.Should().Contain(ScanNominationRules.TopQuartileRotatingSectorReason);
    }

    [Fact]
    public void Evaluate_TopQuartileRsOutsideTopSectors_NotNominatedByRuleA()
    {
        var universe = TenTickerUniverse();
        universe[7] = Entry("T08", rs: 0.08m, sectorRank: Options.ScanTopRotatingSectors + 1);

        var nominations = ScanNominationRules.Evaluate(universe, Options);

        nominations.Should().NotContain(n => n.Ticker == "T08");
    }

    [Fact]
    public void Evaluate_BreakoutOnAboveAverageVolume_Nominated()
    {
        var universe = TenTickerUniverse();
        universe[0] = Entry("T01", rs: 0.01m, distanceFrom63dHigh: 0m, volumeRatio: 1.5m);

        var nominations = ScanNominationRules.Evaluate(universe, Options);

        nominations.Should().Contain(n => n.Ticker == "T01")
            .Which.Reasons.Should().ContainSingle()
            .Which.Should().Be(ScanNominationRules.BreakoutReason);
    }

    [Fact]
    public void Evaluate_BreakoutOnWeakVolume_NotNominated()
    {
        var universe = TenTickerUniverse();
        universe[0] = Entry("T01", rs: 0.01m, distanceFrom63dHigh: 0m, volumeRatio: 0.8m);

        ScanNominationRules.Evaluate(universe, Options)
            .Should().NotContain(n => n.Ticker == "T01");
    }

    [Fact]
    public void Evaluate_TwoRulesOneTicker_SingleNominationWithBothReasons()
    {
        var universe = TenTickerUniverse();
        universe[9] = Entry("T10", rs: 0.10m, sectorRank: 1, distanceFrom63dHigh: 0m, volumeRatio: 2m);

        var nominations = ScanNominationRules.Evaluate(universe, Options);

        var t10 = nominations.Where(n => n.Ticker == "T10").ToList();
        t10.Should().ContainSingle();
        t10[0].Reasons.Should().BeEquivalentTo(
        [
            ScanNominationRules.TopQuartileRotatingSectorReason,
            ScanNominationRules.TopDecileRsReason,
            ScanNominationRules.BreakoutReason,
        ]);
    }

    [Fact]
    public void Evaluate_EtfLens_NeverNominated()
    {
        var universe = TenTickerUniverse();
        universe[9] = Entry("XLK", rs: 0.10m, isEtfLens: true, sectorRank: 1, distanceFrom63dHigh: 0m, volumeRatio: 2m);

        ScanNominationRules.Evaluate(universe, Options)
            .Should().NotContain(n => n.Ticker == "XLK");
    }

    [Fact]
    public void Evaluate_StaleSnapshot_NeverNominated()
    {
        var universe = TenTickerUniverse();
        universe[9] = Entry("T10", rs: 0.10m, stale: true);

        ScanNominationRules.Evaluate(universe, Options)
            .Should().NotContain(n => n.Ticker == "T10");
    }

    [Fact]
    public void Evaluate_OrdersByRsPercentileDescending()
    {
        var universe = TenTickerUniverse();
        universe[0] = Entry("T01", rs: 0.01m, distanceFrom63dHigh: 0m, volumeRatio: 2m);

        var nominations = ScanNominationRules.Evaluate(universe, Options);

        nominations.Select(n => n.Ticker).Should().ContainInOrder("T10", "T01");
    }

    [Fact]
    public void Evaluate_MissingRs_ExcludedFromRsRules_ButBreakoutStillFires()
    {
        var universe = TenTickerUniverse();
        universe.Add(Entry("NORS", rs: null, distanceFrom63dHigh: 0m, volumeRatio: 2m));

        var nominations = ScanNominationRules.Evaluate(universe, Options);

        nominations.Should().Contain(n => n.Ticker == "NORS")
            .Which.Reasons.Should().BeEquivalentTo([ScanNominationRules.BreakoutReason]);
    }

    private static ScanNomination Nomination(string ticker, decimal? rsPercentile)
        => new(ticker, [ScanNominationRules.TopDecileRsReason], rsPercentile);

    [Fact]
    public void RankByQualityMomentum_BlendsGradeAndPercentile_AtTheConfiguredWeight()
    {
        var ranked = ScanNominationRules.RankByQualityMomentum(
            [Nomination("AAA", 40m)],
            new Dictionary<string, int?> { ["AAA"] = 90 },
            new OpportunityOptions { ScanQualityWeight = 0.75m });

        ranked.Single().CombinedScore.Should().Be(77.5m);
        ranked.Single().QualityScore.Should().Be(90);
    }

    [Fact]
    public void RankByQualityMomentum_QualityCanOutrankAHigherMomentumName()
    {
        var ranked = ScanNominationRules.RankByQualityMomentum(
            [Nomination("MOMO", 90m), Nomination("QUAL", 70m)],
            new Dictionary<string, int?> { ["MOMO"] = 20, ["QUAL"] = 95 },
            Options);

        ranked.Select(r => r.Ticker).Should().Equal("QUAL", "MOMO");
    }

    [Fact]
    public void RankByQualityMomentum_UngradedNamesFollowEveryGradedName_AndKeepMomentumOrder()
    {
        var ranked = ScanNominationRules.RankByQualityMomentum(
            [Nomination("XRP-USD", 100m), Nomination("SOL-USD", 95m), Nomination("WEAK", 5m)],
            new Dictionary<string, int?> { ["WEAK"] = 10, ["XRP-USD"] = null },
            Options);

        ranked.Select(r => r.Ticker).Should().Equal("WEAK", "XRP-USD", "SOL-USD");
        ranked.Where(r => r.Ticker.EndsWith("-USD", StringComparison.Ordinal))
            .Should().OnlyContain(r => r.CombinedScore == null);
    }

    [Fact]
    public void RankByQualityMomentum_GradedButRsLessName_ScoresNoCombined_AndKeepsItsGrade()
    {
        // A breakout nominates a ticker whose RS window never resolved: half the score is missing, so
        // it ranks with the ungraded tail instead of being blended against a faked zero percentile.
        var ranked = ScanNominationRules.RankByQualityMomentum(
            [Nomination("NORS", null), Nomination("WEAK", 5m)],
            new Dictionary<string, int?> { ["NORS"] = 95, ["WEAK"] = 10 },
            Options);

        ranked.Select(r => r.Ticker).Should().Equal("WEAK", "NORS");
        ranked.Single(r => r.Ticker == "NORS").CombinedScore.Should().BeNull();
        ranked.Single(r => r.Ticker == "NORS").QualityScore.Should().Be(95);
    }

    [Fact]
    public void RankByQualityMomentum_TiesBreakOnTickerSoRunsAreReproducible()
    {
        var ranked = ScanNominationRules.RankByQualityMomentum(
            [Nomination("BBB", 50m), Nomination("AAA", 50m)],
            new Dictionary<string, int?> { ["AAA"] = 60, ["BBB"] = 60 },
            Options);

        ranked.Select(r => r.Ticker).Should().Equal("AAA", "BBB");
    }

    [Fact]
    public void RankByQualityMomentum_TagsOnlyLeadersWithTheQualityReason()
    {
        var ranked = ScanNominationRules.RankByQualityMomentum(
            [Nomination("LEAD", 80m), Nomination("LAG", 80m)],
            new Dictionary<string, int?> { ["LEAD"] = 60, ["LAG"] = 59 },
            new OpportunityOptions { ScanQualityLeaderScore = 60 });

        ranked.Single(r => r.Ticker == "LEAD").Reasons
            .Should().Contain(ScanNominationRules.QualityMomentumReason);
        ranked.Single(r => r.Ticker == "LAG").Reasons
            .Should().NotContain(ScanNominationRules.QualityMomentumReason);
    }

    [Fact]
    public void RankByQualityMomentum_ClampsAnOutOfRangeWeight()
    {
        var ranked = ScanNominationRules.RankByQualityMomentum(
            [Nomination("AAA", 40m)],
            new Dictionary<string, int?> { ["AAA"] = 90 },
            new OpportunityOptions { ScanQualityWeight = 3m });

        ranked.Single().CombinedScore.Should().Be(90m);
    }
}
