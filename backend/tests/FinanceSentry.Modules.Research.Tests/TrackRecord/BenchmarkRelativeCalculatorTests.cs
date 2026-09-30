namespace FinanceSentry.Modules.Research.Tests.TrackRecord;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FluentAssertions;
using Xunit;

public class BenchmarkRelativeCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AsOf = new(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.NewGuid();

    private static readonly FrictionConfig NoFriction = new()
    {
        PerTradeCostBps = 0m,
        ShortTermTaxRate = 0m,
        LongTermTaxRate = 0m,
    };

    private readonly BenchmarkRelativeCalculator sut = new(new ThesisPerformanceCalculator());

    private static TrackRecordPricePoint Point(int daysAgo, decimal subject, decimal benchmark)
        => new(ThesisEventType.Snapshot, Now.AddDays(-daysAgo), subject, benchmark);

    private static TrackRecordSubject Subject(
        string ticker, string sleeve, string gate, params TrackRecordPricePoint[] points)
        => new(Guid.NewGuid(), ticker, sleeve, gate, points);

    private IReadOnlyList<BenchmarkRelativeRecord> Compute(params TrackRecordSubject[] subjects)
        => ComputeWith(NoFriction, subjects);

    private IReadOnlyList<BenchmarkRelativeRecord> ComputeWith(
        FrictionConfig friction, params TrackRecordSubject[] subjects)
        => sut.Compute(UserId, AsOf, Now, "SPY", subjects, friction);

    private static BenchmarkRelativeRecord Row(
        IEnumerable<BenchmarkRelativeRecord> rows, TrackRecordScope scope, string key, TrackRecordWindow window)
        => rows.Single(r => r.Scope == scope && r.ScopeKey == key && r.Window == window);

    [Fact]
    public void SinceInception_SpansFirstToLastPoint()
    {
        var mu = Subject("MU", "Equities", NetExcessGate.NotHeld, Point(60, 100m, 500m), Point(1, 120m, 525m));

        var rows = Compute(mu);

        var row = Row(rows, TrackRecordScope.Thesis, mu.ThesisId.ToString("N"), TrackRecordWindow.SinceInception);
        row.Covered.Should().BeTrue();
        row.ConstituentCount.Should().Be(1);
        row.Label.Should().Be("MU");
        row.ThesisId.Should().Be(mu.ThesisId);
        row.FromTimestamp.Should().Be(Now.AddDays(-60));
        row.ToTimestamp.Should().Be(Now.AddDays(-1));
        row.SubjectReturnPct.Should().Be(20m);
        row.BenchmarkReturnPct.Should().Be(5m);
        row.ExcessReturnPct.Should().Be(15m);
        row.BenchmarkTicker.Should().Be("SPY");
        row.AsOf.Should().Be(AsOf);
    }

    [Fact]
    public void TrailingWindow_AnchorsOnTheLastPointWithinGraceOfTheWindowStart()
    {
        // One month back is 2026-08-28; grace lets the anchor sit up to 2026-08-31 (day 28 ago).
        var mu = Subject(
            "MU", "Equities", NetExcessGate.NotHeld,
            Point(60, 50m, 400m), Point(33, 90m, 480m), Point(29, 100m, 500m), Point(1, 110m, 500m));

        var rows = Compute(mu);

        var oneMonth = Row(rows, TrackRecordScope.Thesis, mu.ThesisId.ToString("N"), TrackRecordWindow.OneMonth);
        oneMonth.FromTimestamp.Should().Be(Now.AddDays(-29));
        oneMonth.SubjectReturnPct.Should().Be(10m);
        oneMonth.ExcessReturnPct.Should().Be(10m);
    }

    [Fact]
    public void TrailingWindow_IsUncovered_WhenTheSeriesIsYoungerThanTheWindow()
    {
        var mu = Subject("MU", "Equities", NetExcessGate.NotHeld, Point(60, 100m, 500m), Point(1, 120m, 525m));

        var rows = Compute(mu);

        var threeMonths = Row(rows, TrackRecordScope.Thesis, mu.ThesisId.ToString("N"), TrackRecordWindow.ThreeMonths);
        threeMonths.Covered.Should().BeFalse();
        threeMonths.ConstituentCount.Should().Be(0);
        threeMonths.ExcessReturnPct.Should().BeNull();
        threeMonths.FromTimestamp.Should().BeNull();

        Row(rows, TrackRecordScope.Thesis, mu.ThesisId.ToString("N"), TrackRecordWindow.OneYear).Covered.Should().BeFalse();
    }

    [Fact]
    public void SinglePoint_LeavesEveryWindowUncovered()
    {
        var mu = Subject("MU", "Equities", NetExcessGate.NotHeld, Point(5, 100m, 500m));

        var rows = Compute(mu);

        rows.Where(r => r.Scope == TrackRecordScope.Thesis).Should().OnlyContain(r => !r.Covered);
        Row(rows, TrackRecordScope.Book, BenchmarkRelativeCalculator.BookKey, TrackRecordWindow.SinceInception)
            .Covered.Should().BeFalse();
    }

    [Fact]
    public void FuturePoints_AreIgnored()
    {
        var mu = Subject(
            "MU", "Equities", NetExcessGate.NotHeld,
            Point(60, 100m, 500m), Point(1, 120m, 525m), Point(-3, 999m, 500m));

        var rows = Compute(mu);

        Row(rows, TrackRecordScope.Thesis, mu.ThesisId.ToString("N"), TrackRecordWindow.SinceInception)
            .SubjectReturnPct.Should().Be(20m);
    }

    [Fact]
    public void NetExcess_IsStoredOnlyForAVerifiedBasis()
    {
        var friction = new FrictionConfig { PerTradeCostBps = 100m, ShortTermTaxRate = 0m, LongTermTaxRate = 0m };
        var verified = Subject("MU", "Equities", NetExcessGate.Verified, Point(60, 100m, 500m), Point(1, 120m, 525m));
        var unverified = Subject("NVDA", "Equities", NetExcessGate.Unverified, Point(60, 100m, 500m), Point(1, 120m, 525m));

        var rows = ComputeWith(friction, verified, unverified);

        var verifiedRow = Row(rows, TrackRecordScope.Thesis, verified.ThesisId.ToString("N"), TrackRecordWindow.SinceInception);
        verifiedRow.NetGate.Should().Be(NetExcessGate.Verified);
        verifiedRow.NetExcessReturnPct.Should().Be(14m);

        var unverifiedRow = Row(rows, TrackRecordScope.Thesis, unverified.ThesisId.ToString("N"), TrackRecordWindow.SinceInception);
        unverifiedRow.NetGate.Should().Be(NetExcessGate.Unverified);
        unverifiedRow.ExcessReturnPct.Should().Be(15m);
        unverifiedRow.NetExcessReturnPct.Should().BeNull();
    }

    [Fact]
    public void Aggregates_AreEqualWeightedOverCoveredConstituents_AndGroupedBySleeve()
    {
        var mu = Subject("MU", "Equities", NetExcessGate.Verified, Point(60, 100m, 500m), Point(1, 120m, 525m));
        var nvda = Subject("NVDA", "Equities", NetExcessGate.Verified, Point(60, 100m, 500m), Point(1, 90m, 525m));
        var btc = Subject("BTC", "Crypto", NetExcessGate.NotHeld, Point(60, 100m, 500m), Point(1, 120m, 525m));
        var fresh = Subject("AMD", "Equities", NetExcessGate.Verified, Point(2, 100m, 500m));

        var rows = Compute(mu, nvda, btc, fresh);

        var book = Row(rows, TrackRecordScope.Book, BenchmarkRelativeCalculator.BookKey, TrackRecordWindow.SinceInception);
        book.Label.Should().Be(BenchmarkRelativeCalculator.BookLabel);
        book.ConstituentCount.Should().Be(3);
        book.SubjectReturnPct.Should().Be(10m);
        book.ExcessReturnPct.Should().Be(5m);
        book.NetGate.Should().Be(NetExcessGate.Incomplete);
        book.NetExcessReturnPct.Should().BeNull();

        var equities = Row(rows, TrackRecordScope.Sleeve, "Equities", TrackRecordWindow.SinceInception);
        equities.ConstituentCount.Should().Be(2);
        equities.ExcessReturnPct.Should().Be(0m);
        equities.NetGate.Should().Be(NetExcessGate.Verified);
        equities.NetExcessReturnPct.Should().NotBeNull();

        var crypto = Row(rows, TrackRecordScope.Sleeve, "Crypto", TrackRecordWindow.SinceInception);
        crypto.ConstituentCount.Should().Be(1);
        crypto.ExcessReturnPct.Should().Be(15m);
        crypto.NetGate.Should().Be(NetExcessGate.NotHeld);
    }

    [Fact]
    public void Compute_EmitsEveryWindowForEveryScope()
    {
        var mu = Subject("MU", "Equities", NetExcessGate.NotHeld, Point(60, 100m, 500m), Point(1, 120m, 525m));

        var rows = Compute(mu);

        rows.Should().HaveCount(BenchmarkRelativeCalculator.Windows.Count * 3);
        rows.Select(r => (r.Scope, r.ScopeKey, r.Window)).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ApplyUnderperformance_ChainsRunsFromThePreviousRun_AndFlagsSustained()
    {
        var config = new RelativePerformanceConfig { ThresholdPct = 5m, SustainedRuns = 4 };
        var previous = new[]
        {
            Record(TrackRecordScope.Book, "book", excess: -6m, runs: 3),
            Record(TrackRecordScope.Sleeve, "Equities", excess: -6m, runs: 2),
        };
        var current = new[]
        {
            Record(TrackRecordScope.Book, "book", excess: -5m),
            Record(TrackRecordScope.Sleeve, "Equities", excess: -4.99m),
            Record(TrackRecordScope.Sleeve, "Crypto", excess: -9m),
        };

        BenchmarkRelativeCalculator.ApplyUnderperformance(current, previous, config);

        current[0].UnderperformingRuns.Should().Be(4);
        current[0].SustainedUnderperformance.Should().BeTrue();
        current[1].UnderperformingRuns.Should().Be(0);
        current[1].SustainedUnderperformance.Should().BeFalse();
        current[2].UnderperformingRuns.Should().Be(1);
        current[2].SustainedUnderperformance.Should().BeFalse();
    }

    [Fact]
    public void ApplyUnderperformance_UncoveredWindowBreaksTheChain()
    {
        var config = new RelativePerformanceConfig();
        var previous = new[] { Record(TrackRecordScope.Book, "book", excess: -10m, runs: 5) };
        var current = new[] { Record(TrackRecordScope.Book, "book", excess: null) };

        BenchmarkRelativeCalculator.ApplyUnderperformance(current, previous, config);

        current[0].UnderperformingRuns.Should().Be(0);
        current[0].SustainedUnderperformance.Should().BeFalse();
    }

    private static BenchmarkRelativeRecord Record(
        TrackRecordScope scope, string key, decimal? excess, int runs = 0)
        => new()
        {
            UserId = UserId,
            Scope = scope,
            ScopeKey = key,
            Label = key,
            Window = TrackRecordWindow.ThreeMonths,
            Covered = excess is not null,
            ExcessReturnPct = excess,
            UnderperformingRuns = runs,
        };
}
