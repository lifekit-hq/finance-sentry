namespace FinanceSentry.Modules.Research.Tests.Opportunity;

using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// #558 clause 4: stage 1 reaches past the book, so a nightly scan can clear the top-tier bar on
/// several unfamiliar names at once — one Alert each, per user. The signal is the record and stays
/// unconditional; the Alert is the interruption and rides <see cref="ScanAlertMode"/>, the way Radar's
/// <c>ScannerMode</c> launched the structure scanner.
///
/// The gate is scoped to candidates the machine originated: a nomination a person or Ledger made is a
/// deliberate act that was already asked for, so it alerts regardless of the mode — including on the
/// nights the scan re-scores it under its own source.
/// </summary>
public sealed class ScanAlertModeTests
{
    private const string Ticker = "CNST";
    private const string TopTierSignalType = "top_tier_candidate";

    /// <summary>The scorer grades a revenue YoY of +45% at 95 — above the default top-tier bar of 80.</summary>
    private const decimal TopTierRevenueYoy = 0.45m;

    /// <summary>Flat revenue grades well below the bar, so nothing reaches the Alert lane at all.</summary>
    private const decimal BelowBarRevenueYoy = 0m;

    [Fact]
    public async Task ATopTierScanNomination_RecordsItsSignalButRaisesNoAlert_InLogOnly()
    {
        var (alerts, signals) = await ScoreAsync(CandidateSource.Scan, ScanAlertMode.LogOnly, TopTierRevenueYoy);

        signals.Signals.Should().Contain(s => s.SignalType == TopTierSignalType,
            "log-only withholds the interruption, not the record — the finding is still readable");
        alerts.OpportunityAlertCalls.Should().Be(0);
    }

    [Fact]
    public async Task ATopTierScanNomination_RaisesItsAlert_OnceTheModeIsOpened()
    {
        var (alerts, signals) = await ScoreAsync(CandidateSource.Scan, ScanAlertMode.Alerting, TopTierRevenueYoy);

        signals.Signals.Should().Contain(s => s.SignalType == TopTierSignalType);
        alerts.OpportunityAlertCalls.Should().Be(1);
    }

    [Theory]
    [InlineData(CandidateSource.User)]
    [InlineData(CandidateSource.Ledger)]
    public async Task ADeliberateNomination_AlertsEvenInLogOnly(CandidateSource source)
    {
        var (alerts, _) = await ScoreAsync(source, ScanAlertMode.LogOnly, TopTierRevenueYoy);

        alerts.OpportunityAlertCalls.Should().Be(1,
            "the mode guards the machine's fan-out, not a nomination someone asked for");
    }

    /// <summary>
    /// The scan re-scores every nominated ticker nightly under <see cref="CandidateSource.Scan"/>,
    /// including a candidate the user created — <c>UpsertActiveAsync</c> returns the existing row and
    /// keeps its original source. Gating on the command's source instead of the candidate's would mute
    /// the user's own name on every night but the first.
    /// </summary>
    [Fact]
    public async Task AUserCandidateReScoredByTheScan_StillAlerts_InLogOnly()
    {
        var candidates = new FakeCandidateRepository();
        var userId = Guid.NewGuid();
        await candidates.UpsertActiveAsync(userId, Ticker, CandidateSource.User, TimeSpan.FromDays(30));

        var (alerts, _) = await ScoreAsync(
            CandidateSource.Scan, ScanAlertMode.LogOnly, TopTierRevenueYoy, candidates, userId);

        alerts.OpportunityAlertCalls.Should().Be(1, "the user asked for this ticker; the scan only re-graded it");
    }

    /// <summary>
    /// The gate is additive: opening the mode does not turn the top-tier bar off, so a middling scan
    /// nomination still reaches neither the notable signal nor the Alert.
    /// </summary>
    [Fact]
    public async Task ABelowBarScanNomination_ReachesNeitherSignalNorAlert_EvenWhenAlerting()
    {
        var (alerts, signals) = await ScoreAsync(CandidateSource.Scan, ScanAlertMode.Alerting, BelowBarRevenueYoy);

        signals.Signals.Should().NotContain(s => s.SignalType == TopTierSignalType);
        alerts.OpportunityAlertCalls.Should().Be(0);
    }

    private static async Task<(FakeOpportunityAlertGenerator Alerts, RecordingRadarSignalWriter Signals)> ScoreAsync(
        CandidateSource source,
        ScanAlertMode mode,
        decimal revenueYoy,
        FakeCandidateRepository? candidates = null,
        Guid? userId = null)
    {
        var alerts = new FakeOpportunityAlertGenerator();
        var signals = new RecordingRadarSignalWriter();
        var handler = new ScoreCandidateCommandHandler(
            candidates ?? new FakeCandidateRepository(),
            new FakeCandidateScoreRepository(),
            new FakeMarketStructureReader(),
            new FakeSecEdgarService(RevenueGrowthFacts(revenueYoy)),
            new FakeIpsRepository(),
            new FakePositionCapSource(),
            new FakeBrokerageHoldingsReader(),
            signals,
            new FakeMarketRegimeSource(),
            new RecordingThesisEventRecorder(),
            alerts,
            Options.Create(new OpportunityOptions { ScanAlertMode = mode }));

        await handler.Handle(
            new ScoreCandidateCommand(userId ?? Guid.NewGuid(), Ticker, Source: source), CancellationToken.None);

        return (alerts, signals);
    }

    /// <summary>Two comparable quarters, so <see cref="FundamentalsScorer"/> has a year-on-year to grade.</summary>
    private static IReadOnlyList<FundamentalFact> RevenueGrowthFacts(decimal revenueYoy)
        => [
            new(Ticker, "Revenue", "Revenue", "USD", 100m * (1m + revenueYoy), new DateOnly(2026, 5, 31), "Q2", 2026, "10-Q"),
            new(Ticker, "Revenue", "Revenue", "USD", 100m, new DateOnly(2025, 5, 31), "Q2", 2025, "10-Q"),
        ];
}
