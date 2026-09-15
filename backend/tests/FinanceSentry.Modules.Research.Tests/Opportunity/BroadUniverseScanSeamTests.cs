namespace FinanceSentry.Modules.Research.Tests.Opportunity;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FluentAssertions;
using Xunit;

/// <summary>
/// The whole point of #558: a name Denys neither holds nor watches must be able to win a Scan slot.
/// Both halves of that were unit-tested in isolation — Radar's universe composition against a mocked
/// constituent source, Research's rules against hand-built <see cref="UniverseStructureEntry"/>
/// values — which left the seam between them untested: nothing proved that a persisted
/// <see cref="UniverseKind.IndexConstituent"/> member actually comes back out of the real
/// <see cref="MarketStructureReader.GetUniverseStructuresAsync"/> as a non-ETF-lens, rankable entry.
/// These tests drive the real composition → persistence → structure-read → nomination chain, over the
/// <see cref="BroadUniverseRadarFixture"/>. <see cref="BroadUniverseScanCycleTests"/> carries the same
/// universe the rest of the way, through the real scan job and scorer to a persisted candidate row.
/// </summary>
public sealed class BroadUniverseScanSeamTests
{
    private const string Constituent = BroadUniverseRadarFixture.Constituent;
    private const int ConstituentGrade = 91;
    private const int HoldingGrade = 40;

    [Fact]
    public async Task AnIndexConstituentSurfacesAsARankableNonEtfLensEntry()
    {
        var universe = await ReadUniverseStructuresAsync();

        var entry = universe.Should().ContainSingle(e => e.Ticker == Constituent).Subject;
        entry.IsEtfLens.Should().BeFalse(
            "IndexConstituent is an ordinary ticker, not a lens the scanner reads the market through");
        // Left at the production freshness bound: `Evaluate` drops stale entries, so a test that
        // widened the bound would assert freshness it had itself guaranteed.
        entry.Snapshot.Stale.Should().BeFalse();
        entry.Snapshot.RsByWindow.Should().ContainKey(ScanNominationRules.RsWindowBars)
            .WhoseValue.Should().NotBeNull("without an RS value the ranking has no momentum half to score");

        universe.Where(e => e.IsEtfLens).Select(e => e.Ticker)
            .Should().BeEquivalentTo(
                [BroadUniverseRadarFixture.Benchmark, BroadUniverseRadarFixture.Sector],
                "only the seed ETFs are lenses");
    }

    [Fact]
    public async Task AConstituentThatOutrunsTheBookIsNominatedAndTheLaggingHoldingIsNot()
    {
        var universe = await ReadUniverseStructuresAsync();

        var nominations = ScanNominationRules.Evaluate(universe, new OpportunityOptions());

        nominations.Select(n => n.Ticker).Should().BeEquivalentTo(
            [Constituent],
            "the scan must nominate a name outside the book, and must not nominate the lagging holding or the ETF lenses");
        nominations[0].RsPercentile.Should().Be(100m);
    }

    [Fact]
    public async Task TheNominatedConstituentCarriesACombinedQualityMomentumScore()
    {
        var universe = await ReadUniverseStructuresAsync();
        var options = new OpportunityOptions();

        var ranked = ScanNominationRules.RankByQualityMomentum(
            ScanNominationRules.Evaluate(universe, options),
            new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase)
            {
                [Constituent] = ConstituentGrade,
                [BroadUniverseRadarFixture.Holding] = HoldingGrade,
            },
            options);

        var top = ranked.Should().ContainSingle().Subject;
        top.Ticker.Should().Be(Constituent);
        top.QualityScore.Should().Be(ConstituentGrade);
        // grade x 0.5 + RS percentile x 0.5, the default weighting.
        top.CombinedScore.Should().Be(95.5m);
        top.Reasons.Should().Contain(ScanNominationRules.QualityMomentumReason);
    }

    private static async Task<IReadOnlyList<UniverseStructureEntry>> ReadUniverseStructuresAsync()
    {
        await using var radar = await BroadUniverseRadarFixture.CreateAsync();
        return await radar.Reader.GetUniverseStructuresAsync(CancellationToken.None);
    }
}
