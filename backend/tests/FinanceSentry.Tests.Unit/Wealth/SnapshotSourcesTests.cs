namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Wealth.Domain;
using FluentAssertions;
using Xunit;

/// <summary>
/// The per-source view of a stored snapshot (framework §5.6, step 3): rows when the snapshot has them, the sleeve
/// columns and <c>StaleSleeves</c> when it predates them, and the same stale state and signals either way.
/// </summary>
public class SnapshotSourcesTests
{
    private static NetWorthSnapshot Snapshot(
        decimal banking, decimal brokerage, decimal crypto, string? staleSleeves = null,
        bool isApproximate = false, FxStatus? fx = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        SnapshotDate = new DateOnly(2026, 10, 15),
        BankingTotal = banking,
        BrokerageTotal = brokerage,
        CryptoTotal = crypto,
        TotalNetWorth = banking + brokerage + crypto,
        Currency = "USD",
        StaleSleeves = staleSleeves,
        IsApproximate = isApproximate,
        FxStatus = fx,
    };

    private static NetWorthSnapshotSource Row(
        NetWorthSnapshot snapshot, string sourceId, EconomyKind kind, decimal? total,
        SourceHealth health = SourceHealth.Complete, params string[] reasons) => new()
    {
        Id = Guid.NewGuid(),
        SnapshotId = snapshot.Id,
        SourceId = sourceId,
        Kind = kind,
        Total = total,
        Health = health,
        Reasons = reasons,
    };

    [Fact]
    public void ASnapshotWithoutRows_ListsTheThreeSleevesFromItsColumns_AndMarksTheCarriedOnesStale()
    {
        var snapshot = Snapshot(1_000m, 500m, 250m, staleSleeves: "brokerage,crypto");

        var sources = SnapshotSources.Of(snapshot, null);

        sources.Sources.Should().BeEquivalentTo(
            [
                new SnapshotSourceFigure("banking", EconomyKind.Bank, 1_000m, SourceHealth.Complete, []),
                new SnapshotSourceFigure("brokerage", EconomyKind.Brokerage, 500m, SourceHealth.Degraded, [SourceReasons.StaleSource]),
                new SnapshotSourceFigure("crypto", EconomyKind.CryptoVenue, 250m, SourceHealth.Degraded, [SourceReasons.StaleSource]),
            ],
            o => o.WithStrictOrdering());
        sources.StaleSources.Should().Equal("brokerage", "crypto");
        sources.StaleSleeves.Should().Be("brokerage,crypto");
    }

    [Theory]
    [InlineData(null, false, null)]
    [InlineData("crypto", false, FxStatus.Exact)]
    [InlineData("banking,brokerage,crypto", false, FxStatus.TodayRate)]
    [InlineData(null, true, null)]
    [InlineData("brokerage", false, FxStatus.Unknown1To1)]
    public void ASnapshotWithoutRows_GivesTheSameSignalsAsItsColumns(string? staleSleeves, bool isApproximate, FxStatus? fx)
    {
        var snapshot = Snapshot(1_000m, 500m, 250m, staleSleeves, isApproximate, fx);

        var sources = SnapshotSources.Of(snapshot, []);

        sources.StaleSleeves.Should().Be(staleSleeves);
        HeadlineQualityMapper.Assess(sources.Signals(snapshot), true).Should().BeEquivalentTo(
            HeadlineQualityMapper.Assess(HeadlineSignals.FromSnapshot(staleSleeves, isApproximate, fx), true));
    }

    [Fact]
    public void ACarriedForwardSleeve_CountsTheColumnsFigure_AndKeepsTheRowsHealthAndReasons()
    {
        // The crypto sleeve was carried forward: the row holds this run's reading (0), the column yesterday's 400.
        var snapshot = Snapshot(1_000m, 500m, 400m, staleSleeves: "crypto", fx: FxStatus.TodayRate);
        var rows = new[]
        {
            Row(snapshot, "crypto", EconomyKind.CryptoVenue, 0m, SourceHealth.Degraded, FxReasons.TodayRate, SourceReasons.StaleSource),
            Row(snapshot, "brokerage", EconomyKind.Brokerage, 500m),
            Row(snapshot, "banking", EconomyKind.Bank, 1_000m, SourceHealth.Estimated, FxReasons.TodayRate),
        };

        var sources = SnapshotSources.Of(snapshot, rows);

        sources.Sources.Select(s => s.SourceId).Should().Equal("banking", "brokerage", "crypto");
        var crypto = sources.Sources.Single(s => s.SourceId == "crypto");
        crypto.Total.Should().Be(400m, "the snapshot counted the carried figure");
        crypto.Health.Should().Be(SourceHealth.Degraded);
        crypto.Reasons.Should().Equal(FxReasons.TodayRate, SourceReasons.StaleSource);
        sources.Sources.Single(s => s.SourceId == "banking").Health.Should().Be(SourceHealth.Estimated);
        sources.Sources.Where(s => SnapshotSources.SleeveIds.Contains(s.SourceId)).Sum(s => s.Total)
            .Should().Be(snapshot.TotalNetWorth);
        sources.StaleSleeves.Should().Be(snapshot.StaleSleeves);
        HeadlineQualityMapper.Assess(sources.Signals(snapshot), true).Should().BeEquivalentTo(
            HeadlineQualityMapper.Assess(HeadlineSignals.FromSnapshot(snapshot.StaleSleeves, false, snapshot.FxStatus), true));
    }

    [Fact]
    public void AFailedRead_CountsTheCarriedColumn_AndStaysUnavailable()
    {
        var snapshot = Snapshot(1_000m, 700m, 0m, staleSleeves: "brokerage");
        var rows = new[]
        {
            Row(snapshot, "banking", EconomyKind.Bank, 1_000m),
            Row(snapshot, "brokerage", EconomyKind.Brokerage, null, SourceHealth.Unavailable, SourceReasons.StaleSource),
            Row(snapshot, "crypto", EconomyKind.CryptoVenue, 0m),
        };

        var brokerage = SnapshotSources.Of(snapshot, rows).Sources.Single(s => s.SourceId == "brokerage");

        brokerage.Total.Should().Be(700m);
        brokerage.Health.Should().Be(SourceHealth.Unavailable);
        brokerage.IsStale.Should().BeTrue();
    }

    [Fact]
    public void ASleeveWithNoRow_FallsBackToItsColumn()
    {
        var snapshot = Snapshot(1_000m, 500m, 250m, staleSleeves: "crypto");

        var sources = SnapshotSources.Of(snapshot, [Row(snapshot, "banking", EconomyKind.Bank, 1_000m)]);

        sources.Sources.Should().HaveCount(3);
        sources.Sources.Single(s => s.SourceId == "crypto").Should().BeEquivalentTo(
            new SnapshotSourceFigure("crypto", EconomyKind.CryptoVenue, 250m, SourceHealth.Degraded, [SourceReasons.StaleSource]));
        sources.StaleSleeves.Should().Be("crypto");
    }

    [Fact]
    public void ASourceBeyondTheSleeves_FollowsThem_AndIsStaleWithoutEnteringTheLegacyValue()
    {
        var snapshot = Snapshot(1_000m, 0m, 0m);
        var rows = new[]
        {
            Row(snapshot, "inzhur", EconomyKind.Fund, null, SourceHealth.Unavailable, SourceReasons.StaleSource),
            Row(snapshot, "banking", EconomyKind.Bank, 1_000m),
            Row(snapshot, "brokerage", EconomyKind.Brokerage, 0m),
            Row(snapshot, "crypto", EconomyKind.CryptoVenue, 0m),
        };

        var sources = SnapshotSources.Of(snapshot, rows);

        sources.Sources.Select(s => s.SourceId).Should().Equal("banking", "brokerage", "crypto", "inzhur");
        sources.Sources[^1].Total.Should().BeNull();
        sources.StaleSources.Should().Equal("inzhur");
        sources.StaleSleeves.Should().BeNull("the legacy value names only the three sleeves");
        HeadlineQualityMapper.Assess(sources.Signals(snapshot), true).Reasons
            .Should().Equal(new HeadlineReason(HeadlineReasonCode.StaleSource, "inzhur"));
    }

    [Fact]
    public void OfEach_PairsEverySnapshotWithItsOwnRows_InOrder()
    {
        var old = Snapshot(900m, 0m, 0m, staleSleeves: "banking");
        var current = Snapshot(1_000m, 500m, 400m, staleSleeves: "crypto");
        var rows = new[]
        {
            Row(current, "banking", EconomyKind.Bank, 1_000m),
            Row(current, "brokerage", EconomyKind.Brokerage, 500m),
            Row(current, "crypto", EconomyKind.CryptoVenue, 0m, SourceHealth.Degraded, SourceReasons.StaleSource),
        };

        var each = SnapshotSources.OfEach([old, current], rows);

        each.Select(x => x.Snapshot).Should().Equal(old, current);
        each[0].Sources.StaleSleeves.Should().Be("banking");
        each[1].Sources.StaleSleeves.Should().Be("crypto");
        each[1].Sources.Sources.Single(s => s.SourceId == "crypto").Total.Should().Be(400m);
    }
}
