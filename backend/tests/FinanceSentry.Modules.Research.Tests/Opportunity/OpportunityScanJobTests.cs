namespace FinanceSentry.Modules.Research.Tests.Opportunity;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// 019 US2 / FR-006: the scan ranks the whole ingested universe by combined quality x momentum, so a
/// name that is neither held nor watchlisted — an index constituent that only exists in the universe
/// because broad ingestion put bars behind it — can take a nomination slot from a momentum leader.
/// </summary>
public sealed class OpportunityScanJobTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static UniverseStructureEntry Entry(string ticker, decimal rs)
        => new(
            ticker,
            IsEtfLens: false,
            new MarketStructureSnapshot(
                ticker,
                new Dictionary<int, decimal?> { [ScanNominationRules.RsWindowBars] = rs },
                new Dictionary<int, decimal?>(),
                ExtensionFromMa50: 0.05m,
                TodayZScore: 0m,
                VolumeRatio: 1.5m,
                Ma50: 50m,
                Ma200: 48m,
                Stale: false,
                SectorRank: null,
                SectorRankDelta: null,
                // At the 63-day high on 1.5x volume: every member qualifies on the breakout rule, so
                // the nomination set is the whole universe and ordering is purely the ranked score.
                DistanceFrom63dHigh: 0m));

    /// <summary>Revenue-only facts: the scorer maps a YoY of <paramref name="revenueYoy"/> to 50 + yoy*100.</summary>
    private static IReadOnlyList<FundamentalFact> RevenueGrowthFacts(string ticker, decimal revenueYoy)
        => [
            new(ticker, "Revenue", "Revenue", "USD", 100m * (1m + revenueYoy), new DateOnly(2026, 5, 31), "Q2", 2026, "10-Q"),
            new(ticker, "Revenue", "Revenue", "USD", 100m, new DateOnly(2025, 5, 31), "Q2", 2025, "10-Q"),
        ];

    private static (OpportunityScanJob Job, RecordingScoreCandidateHandler Scorer, RecordingSecEdgarService Edgar) BuildJob(
        IReadOnlyList<UniverseStructureEntry> universe,
        IReadOnlyDictionary<string, IReadOnlyList<FundamentalFact>> facts,
        OpportunityOptions options,
        IReadOnlyCollection<string>? edgarFailures = null)
    {
        var scorer = new RecordingScoreCandidateHandler();
        var edgar = new RecordingSecEdgarService(facts, edgarFailures);
        var job = new OpportunityScanJob(
            new FakeUniverseStructureReader(universe),
            edgar,
            new FakeIpsRepository(new InvestmentPolicyStatement { UserId = UserId }),
            scorer,
            Options.Create(options),
            NullLogger<OpportunityScanJob>.Instance);

        return (job, scorer, edgar);
    }

    [Fact]
    public async Task ExecuteAsync_QualityLiftsAConstituentOverAPurerMomentumName()
    {
        // Percentiles 100 / 66.67 / 33.33. Momentum alone would nominate LOWQ then MIDQ; QUAL grades
        // 95 against their 50, so combined (w=0.5) it scores 64.17 to MIDQ's 58.33 and takes the slot.
        var universe = new List<UniverseStructureEntry>
        {
            Entry("LOWQ", 0.10m),
            Entry("MIDQ", 0.09m),
            Entry("QUAL", 0.08m),
        };
        var facts = new Dictionary<string, IReadOnlyList<FundamentalFact>>(StringComparer.OrdinalIgnoreCase)
        {
            ["LOWQ"] = RevenueGrowthFacts("LOWQ", 0m),
            ["MIDQ"] = RevenueGrowthFacts("MIDQ", 0m),
            ["QUAL"] = RevenueGrowthFacts("QUAL", 0.45m),
        };
        var options = new OpportunityOptions { ScanMaxNominationsPerRun = 2 };

        var (job, scorer, _) = BuildJob(universe, facts, options);
        await job.ExecuteAsync();

        scorer.Commands.Select(c => c.Ticker).Should().Equal("LOWQ", "QUAL");
        scorer.Commands.Single(c => c.Ticker == "QUAL").NominationReasons
            .Should().Contain(ScanNominationRules.QualityMomentumReason);
        scorer.Commands.Single(c => c.Ticker == "LOWQ").NominationReasons
            .Should().NotContain(ScanNominationRules.QualityMomentumReason);
        scorer.Commands.Should().OnlyContain(c => c.Source == CandidateSource.Scan);
    }

    [Fact]
    public async Task ExecuteAsync_GradesAtMostTheShortlist_SoEdgarFanOutStaysBounded()
    {
        var universe = Enumerable.Range(1, 20).Select(i => Entry($"T{i:00}", 0.01m * i)).ToList();
        var options = new OpportunityOptions { ScanQualityShortlistSize = 3, ScanMaxNominationsPerRun = 2 };

        var (job, scorer, edgar) = BuildJob(
            universe, new Dictionary<string, IReadOnlyList<FundamentalFact>>(), options);
        await job.ExecuteAsync();

        // Only the top-3 momentum names cost an EDGAR call, and the cap still applies after re-ranking.
        edgar.FundamentalsRequests.Should().Equal("T20", "T19", "T18");
        scorer.Commands.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteAsync_ShortlistBelowTheCap_StillFillsTheCap()
    {
        var universe = Enumerable.Range(1, 10).Select(i => Entry($"T{i:00}", 0.01m * i)).ToList();
        var options = new OpportunityOptions { ScanQualityShortlistSize = 1, ScanMaxNominationsPerRun = 4 };

        var (job, scorer, edgar) = BuildJob(
            universe, new Dictionary<string, IReadOnlyList<FundamentalFact>>(), options);
        await job.ExecuteAsync();

        // A shortlist smaller than the cap bounds the EDGAR fan-out, never the nomination count.
        scorer.Commands.Should().HaveCount(4);
        edgar.FundamentalsRequests.Should().HaveCount(4);
    }

    [Fact]
    public async Task ExecuteAsync_UngradableTicker_KeepsMomentumStandingBelowGradedNames()
    {
        // XRP-USD has no EDGAR filer and BROKE fails upstream: both stay nominatable on momentum
        // alone, but a graded name outranks them however high their RS.
        var universe = new List<UniverseStructureEntry>
        {
            Entry("XRP-USD", 0.10m),
            Entry("BROKE", 0.09m),
            Entry("GRADED", 0.01m),
        };
        var facts = new Dictionary<string, IReadOnlyList<FundamentalFact>>(StringComparer.OrdinalIgnoreCase)
        {
            ["GRADED"] = RevenueGrowthFacts("GRADED", 0.10m),
        };

        var (job, scorer, _) = BuildJob(
            universe, facts, new OpportunityOptions(), edgarFailures: ["BROKE"]);
        await job.ExecuteAsync();

        scorer.Commands.Select(c => c.Ticker).Should().Equal("GRADED", "XRP-USD", "BROKE");
    }

    [Fact]
    public async Task ExecuteAsync_EmptyUniverse_ScoresNothingAndSpendsNoEdgarCall()
    {
        var (job, scorer, edgar) = BuildJob(
            [], new Dictionary<string, IReadOnlyList<FundamentalFact>>(), new OpportunityOptions());
        await job.ExecuteAsync();

        scorer.Commands.Should().BeEmpty();
        edgar.FundamentalsRequests.Should().BeEmpty();
    }
}
