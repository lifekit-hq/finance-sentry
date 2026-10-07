namespace FinanceSentry.Tests.Unit.Radar;

using FluentAssertions;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Ports;
using Xunit;

/// <summary>
/// Unit tests for the weekly brief composition (feature 414). Exercises
/// <see cref="PerformanceBriefComposer"/> directly — no Hangfire, no DB.
/// Payload percentages are percentage points (0–100), matching what the portfolio scanner writes.
/// </summary>
public sealed class PerformanceBriefComposerTests
{
    private static BookPerformanceResult OneWeekResult(
        decimal bookTwr = 0.03m, decimal spyTwr = 0.01m) =>
        new(
            [new PeriodTwr(
                BookPerformancePeriod.OneWeek,
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)),
                bookTwr, spyTwr,
                bookTwr - spyTwr,
                bookTwr > spyTwr + 0.001m ? "outperform"
                    : bookTwr < spyTwr - 0.001m ? "underperform" : "inline")],
            DateOnly.FromDateTime(DateTime.UtcNow));

    private static BookPerformanceResult FourPeriodResult() =>
        new(
            [
                new PeriodTwr(BookPerformancePeriod.OneWeek, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)),
                    0.03m, 0.01m, 0.02m, "outperform"),
                new PeriodTwr(BookPerformancePeriod.OneMonth, DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1)),
                    0.05m, 0.04m, 0.01m, "outperform"),
                new PeriodTwr(BookPerformancePeriod.ThreeMonths, DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-3)),
                    0.10m, 0.08m, 0.02m, "outperform"),
                new PeriodTwr(BookPerformancePeriod.OneYear, DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)),
                    0.25m, 0.20m, 0.05m, "outperform"),
            ],
            DateOnly.FromDateTime(DateTime.UtcNow));

    private static RadarSignal Signal(
        string signalType, string subject, Dictionary<string, object> payload,
        DateTimeOffset? timestamp = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Scanner = RadarScanners.Portfolio,
            SignalType = signalType,
            Severity = SignalSeverity.Notable,
            SubjectType = RadarSubjectTypes.AssetClass,
            Subject = subject,
            UserId = Guid.NewGuid(),
            DedupKey = $"test:{signalType}:{subject}",
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Payload = payload,
        };

    private static RadarSignal DriftSignal(
        string subject, string status, decimal driftPct,
        DateTimeOffset? timestamp = null, decimal totalUsd = 100_000m) =>
        Signal(
            RadarSignalTypes.AllocationDrift,
            subject,
            new Dictionary<string, object>
            {
                ["status"] = status,
                ["driftPct"] = driftPct,
                ["targetPct"] = 40m,
                ["actualPct"] = 40m + driftPct,
                ["totalUsd"] = totalUsd,
            },
            timestamp);

    private static RadarSignal CashBufferSignal(
        decimal cashPct, decimal floorPct, bool compliant, decimal totalUsd = 100_000m) =>
        Signal(
            RadarSignalTypes.CashBuffer,
            "portfolio",
            new Dictionary<string, object>
            {
                ["cashPct"] = cashPct,
                ["cashUsd"] = totalUsd * cashPct / 100m,
                ["minCashBufferPct"] = floorPct,
                ["totalUsd"] = totalUsd,
                ["compliant"] = compliant,
            });

    private static RadarSignal ConcentrationSignal(
        string symbol, decimal weightPct, decimal limitPct, bool overLimit) =>
        Signal(
            RadarSignalTypes.ConcentrationWeight,
            symbol,
            new Dictionary<string, object>
            {
                ["weightPct"] = weightPct,
                ["usdValue"] = 22_000m,
                ["limitPct"] = limitPct,
                ["overLimit"] = overLimit,
            });

    private static string[] BodyLines(PerformanceBrief brief) => brief.Body.Split('\n');

    [Fact]
    public void Headline_IncludesVerdictAndDelta()
    {
        var brief = PerformanceBriefComposer.Compose(OneWeekResult(0.03m, 0.01m), [], null);

        brief.Headline.Should().StartWith("Weekly brief:");
        brief.Headline.Should().Contain("Outperform");
        brief.Headline.Should().Contain("vs SPY");
    }

    [Fact]
    public void Body_ContainsFourPeriodLines_WhenAllPeriodsPresent()
    {
        var lines = BodyLines(PerformanceBriefComposer.Compose(FourPeriodResult(), [], null));

        lines.Should().Contain(l => l.StartsWith("• 1W:"));
        lines.Should().Contain(l => l.StartsWith("• 1M:"));
        lines.Should().Contain(l => l.StartsWith("• 3M:"));
        lines.Should().Contain(l => l.StartsWith("• 1Y:"));
    }

    [Fact]
    public void AppendsDriftTrend_WhenNotableSignalPresent()
    {
        var brief = PerformanceBriefComposer.Compose(
            OneWeekResult(), [DriftSignal("Equity", "OverBand", 8.3m)], null);

        brief.Body.Should().Contain("Drift: Equity OverBand (+8.3pp vs target)");
    }

    [Fact]
    public void NoDriftLines_WhenNoSignals()
    {
        PerformanceBriefComposer.Compose(OneWeekResult(), [], null)
            .Body.Should().NotContain("Drift:");
    }

    [Fact]
    public void DeduplicatesSubject_TakingMostRecent()
    {
        var older = DriftSignal("Equity", "OverBand", 5m, DateTimeOffset.UtcNow.AddDays(-10));
        var newer = DriftSignal("Equity", "UnderBand", -12m, DateTimeOffset.UtcNow.AddDays(-1));

        var driftLines = BodyLines(PerformanceBriefComposer.Compose(OneWeekResult(), [older, newer], null))
            .Where(l => l.StartsWith("• Drift:")).ToList();

        driftLines.Should().HaveCount(1);
        driftLines[0].Should().Contain("UnderBand");
    }

    [Fact]
    public void SortsDriftLines_ByAbsoluteDriftDescending()
    {
        var small = DriftSignal("Bonds", "UnderBand", -3m);
        var large = DriftSignal("Equity", "OverBand", 15m);

        var lines = BodyLines(PerformanceBriefComposer.Compose(OneWeekResult(), [small, large], null))
            .Where(l => l.StartsWith("• Drift:")).ToList();

        lines.Should().HaveCount(2);
        lines[0].Should().Contain("Equity");
        lines[1].Should().Contain("Bonds");
    }

    [Fact]
    public void DriftPctNegative_ShowsNegativeSign()
    {
        var brief = PerformanceBriefComposer.Compose(
            OneWeekResult(), [DriftSignal("Bonds", "UnderBand", -7m)], null);

        brief.Body.Should().Contain("Drift: Bonds UnderBand (-7pp");
    }

    [Fact]
    public void IgnoresDriftSignals_ThatAreWithinBand()
    {
        var brief = PerformanceBriefComposer.Compose(
            OneWeekResult(), [DriftSignal("Equity", "Within", 1m)], null);

        brief.Body.Should().NotContain("Drift:");
        brief.Body.Should().NotContain("Action:");
    }

    [Fact]
    public void TotalMessageLines_DoNotExceedTwelve_WhenEverySectionIsPresent()
    {
        var manyDrift = Enumerable.Range(1, 10)
            .Select(i => DriftSignal($"Asset{i}", "OverBand", 5m + i))
            .ToList();
        var signals = manyDrift
            .Append(CashBufferSignal(cashPct: 2m, floorPct: 10m, compliant: false))
            .Append(ConcentrationSignal("AAPL", 22.4m, 15m, overLimit: true))
            .ToList();
        var record = new TrackRecordDelta(IsTerminal: true, Count: 12, 58m, 3.2m, LowSample: true);

        var brief = PerformanceBriefComposer.Compose(FourPeriodResult(), signals, record);

        var totalLines = 1 + BodyLines(brief).Length;
        totalLines.Should().BeLessOrEqualTo(12);
        brief.Body.Should().Contain("Calls:");
        brief.Body.Should().Contain("Action:");
    }

    [Fact]
    public void FormatsUnderperformVerdict()
    {
        PerformanceBriefComposer.Compose(OneWeekResult(0.01m, 0.05m), [], null)
            .Headline.Should().Contain("Underperform");
    }

    [Fact]
    public void HeadlineOmitsDelta_WhenWeeklyDataMissing()
    {
        var result = new BookPerformanceResult(
            [new PeriodTwr(
                BookPerformancePeriod.OneMonth,
                DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1)),
                0.05m, 0.04m, 0.01m, "outperform")],
            DateOnly.FromDateTime(DateTime.UtcNow));

        PerformanceBriefComposer.Compose(result, [], null)
            .Headline.Should().Be("Weekly brief: Outperform");
    }

    [Fact]
    public void TrackRecordLine_ReportsClosedCallsWithLowSampleCaveat()
    {
        var record = new TrackRecordDelta(IsTerminal: true, Count: 12, 58m, 3.2m, LowSample: true);

        var brief = PerformanceBriefComposer.Compose(OneWeekResult(), [], record);

        brief.Body.Should().Contain("Calls: 58% of 12 closed beat SPY, avg Δ +3.2% (low sample)");
    }

    [Fact]
    public void TrackRecordLine_LabelsOpenCallsDifferently_AndOmitsCaveatWhenSampleIsSound()
    {
        var record = new TrackRecordDelta(IsTerminal: false, Count: 5, 60m, -1.4m, LowSample: false);

        var brief = PerformanceBriefComposer.Compose(OneWeekResult(), [], record);

        brief.Body.Should().Contain("Calls: 60% of 5 open ahead of SPY, avg Δ -1.4%");
        brief.Body.Should().NotContain("low sample");
    }

    [Fact]
    public void TrackRecordLine_Omitted_WhenNoRecordAvailable()
    {
        PerformanceBriefComposer.Compose(OneWeekResult(), [], null)
            .Body.Should().NotContain("Calls:");
    }

    [Fact]
    public void ActionLine_TrimsTheWorstOverBandSleeve_WithDollarSwing()
    {
        var signals = new[]
        {
            DriftSignal("Bonds", "UnderBand", -3m, totalUsd: 150_000m),
            DriftSignal("Equity", "OverBand", 8.3m, totalUsd: 150_000m),
        };

        var brief = PerformanceBriefComposer.Compose(OneWeekResult(), signals, null);

        brief.Body.Should().Contain("Action: Trim Equity by ~8.3pp (~$12.5k) to its 40% IPS target.");
    }

    [Fact]
    public void ActionLine_AddsToTheWorstUnderBandSleeve()
    {
        var brief = PerformanceBriefComposer.Compose(
            OneWeekResult(), [DriftSignal("Bonds", "UnderBand", -6m, totalUsd: 50_000m)], null);

        brief.Body.Should().Contain("Action: Add ~6pp (~$3k) to Bonds to reach its 40% IPS target.");
    }

    [Fact]
    public void ActionLine_IsSingular_EvenWhenSeveralBreachesExist()
    {
        var signals = new[]
        {
            DriftSignal("Equity", "OverBand", 8.3m),
            CashBufferSignal(cashPct: 2m, floorPct: 10m, compliant: false),
            ConcentrationSignal("AAPL", 22.4m, 15m, overLimit: true),
        };

        var actionLines = BodyLines(PerformanceBriefComposer.Compose(OneWeekResult(), signals, null))
            .Where(l => l.StartsWith("• Action:")).ToList();

        actionLines.Should().HaveCount(1);
        actionLines[0].Should().Contain("Trim Equity");
    }

    [Fact]
    public void ActionLine_FallsBackToCashFloor_WhenAllocationIsWithinBands()
    {
        var signals = new[]
        {
            CashBufferSignal(cashPct: 2.5m, floorPct: 10m, compliant: false),
            ConcentrationSignal("AAPL", 22.4m, 15m, overLimit: true),
        };

        var brief = PerformanceBriefComposer.Compose(OneWeekResult(), signals, null);

        brief.Body.Should().Contain("Action: Rebuild cash to the 10% floor — now 2.5% (~$7.5k short).");
    }

    [Fact]
    public void ActionLine_FallsBackToPositionCap_WhenAllocationAndCashAreCompliant()
    {
        var signals = new[]
        {
            CashBufferSignal(cashPct: 12m, floorPct: 10m, compliant: true),
            ConcentrationSignal("AAPL", 22.4m, 15m, overLimit: true),
        };

        var brief = PerformanceBriefComposer.Compose(OneWeekResult(), signals, null);

        brief.Body.Should().Contain("Action: Trim AAPL to the 15% cap — now 22.4% of the book.");
    }

    [Fact]
    public void ActionLine_Omitted_WhenNothingBreachesPolicy()
    {
        var signals = new[]
        {
            CashBufferSignal(cashPct: 12m, floorPct: 10m, compliant: true),
            ConcentrationSignal("AAPL", 9m, 15m, overLimit: false),
            Signal(RadarSignalTypes.SyncHealth, "portfolio",
                new Dictionary<string, object> { ["isStale"] = true }),
        };

        PerformanceBriefComposer.Compose(OneWeekResult(), signals, null)
            .Body.Should().NotContain("Action:");
    }

    private const string AppBase = "https://app.example.com";

    [Fact]
    public void Bullets_LinkToTheirEntity_WhenAppBaseUrlIsSet()
    {
        var signals = new[]
        {
            DriftSignal("Equity", "OverBand", 8.3m),
            ConcentrationSignal("AAPL", 22.4m, 15m, overLimit: true),
        };
        var record = new TrackRecordDelta(IsTerminal: true, Count: 12, 58m, 3.2m, LowSample: false);

        var lines = BodyLines(PerformanceBriefComposer.Compose(OneWeekResult(), signals, record, AppBase));

        lines.Should().Contain(l => l.StartsWith("• 1W:") && l.EndsWith(" → " + AppBase + "/accounts/investments"));
        lines.Should().Contain(l => l.StartsWith("• Drift: Equity") && l.EndsWith(AppBase + "/accounts/investments"));
        lines.Should().Contain(l => l.StartsWith("• Calls:") && !l.Contains("http"));
        lines.Should().Contain(l => l.StartsWith("• Action: Trim Equity") && l.EndsWith("/accounts/investments"));
    }

    [Fact]
    public void ConcentrationAction_LinksToTheSymbolDossier()
    {
        var brief = PerformanceBriefComposer.Compose(
            OneWeekResult(), [ConcentrationSignal("AAPL", 22.4m, 15m, overLimit: true)], null, AppBase);

        brief.Body.Should().Contain("• Action: Trim AAPL to the 15% cap — now 22.4% of the book. → " + AppBase + "/assets/AAPL");
    }

    [Fact]
    public void Bullets_StayPlain_WhenNoAppBaseUrl()
        => PerformanceBriefComposer.Compose(OneWeekResult(), [DriftSignal("Equity", "OverBand", 8.3m)], null)
            .Body.Should().NotContain("http").And.NotContain("→");
}
