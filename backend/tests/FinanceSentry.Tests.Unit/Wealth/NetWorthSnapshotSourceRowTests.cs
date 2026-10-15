namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Services;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Wealth.Application.Services;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using FinanceSentry.Modules.Wealth.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// The per-source snapshot rows (framework §5.6, step 2): the job hands every registered source's read to the
/// snapshot service, and the service turns each into a row with the source's health and typed reasons.
/// </summary>
public class NetWorthSnapshotSourceRowTests
{
    private static readonly Guid UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateOnly Day = new(2026, 10, 15);

    private static WealthSourceReading Reading(decimal cash, decimal invested = 0m, FxStatus fx = FxStatus.Exact)
        => new(cash, invested == 0m ? [] : [new BookFigurePosition("AAPL", "equity", 1m, null, invested, "ibkr")], fx);

    private sealed class FakeSource(string id, EconomyKind kind, WealthSourceReading? reading) : IWealthSource
    {
        public string SourceId => id;

        public EconomyKind Kind => kind;

        public Task<WealthSourceReading> ReadAsync(Guid userId, CancellationToken ct = default)
            => reading is null
                ? throw new InvalidOperationException("provider down")
                : Task.FromResult(reading);
    }

    private static Mock<INetWorthSnapshotService> CapturingService(Action<NetWorthSnapshotData> capture)
    {
        var service = new Mock<INetWorthSnapshotService>();
        service
            .Setup(s => s.PersistSnapshotAsync(UserId, It.IsAny<NetWorthSnapshotData>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, NetWorthSnapshotData, CancellationToken>((_, data, _) => capture(data))
            .Returns(Task.CompletedTask);
        return service;
    }

    private static NetWorthSnapshotJob JobOver(
        INetWorthSnapshotService service, params IWealthSource[] sources)
        => JobOver(service, DateTime.UtcNow, sources);

    private static NetWorthSnapshotJob JobOver(
        INetWorthSnapshotService service, DateTime bankingLastSync, params IWealthSource[] sources)
    {
        var banking = new Mock<IBankingTotalsReader>();
        banking.Setup(b => b.GetLatestSuccessfulSyncAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(bankingLastSync);
        var crypto = new Mock<ICryptoHoldingsReader>();
        crypto.Setup(c => c.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(
            [new CryptoHoldingSummary("BTC", 1m, 0m, 50m, DateTime.UtcNow, "binance")]);
        var brokerage = new Mock<IBrokerageHoldingsReader>();
        brokerage.Setup(b => b.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(
            [new BrokerageHoldingSummary("AAPL", "equity", 1m, 200m, DateTime.UtcNow, "ibkr")]);
        return new NetWorthSnapshotJob(
            banking.Object, crypto.Object, brokerage.Object, service,
            new BookFiguresService(sources, NullLogger<BookFiguresService>.Instance));
    }

    private static IWealthSource[] ThreeSources(WealthSourceReading? crypto = null) =>
    [
        new FakeSource("banking", EconomyKind.Bank, Reading(cash: 100m)),
        new FakeSource("brokerage", EconomyKind.Brokerage, Reading(cash: 10m, invested: 190m)),
        new FakeSource("crypto", EconomyKind.CryptoVenue, crypto ?? Reading(cash: 0m, invested: 50m)),
    ];

    [Fact]
    public async Task Job_HandsOneFigurePerRegisteredSource_InRegistrationOrder_WithNoChangeForAFourthSource()
    {
        NetWorthSnapshotData? captured = null;
        var job = JobOver(
            CapturingService(d => captured = d).Object,
            [.. ThreeSources(), new FakeSource("fund", EconomyKind.Fund, Reading(cash: 0m, invested: 700m))]);

        await job.CaptureForUserAsync(UserId, Day);

        captured!.Sources!.Select(f => (f.SourceId, f.Kind)).Should().Equal(
            ("banking", EconomyKind.Bank),
            ("brokerage", EconomyKind.Brokerage),
            ("crypto", EconomyKind.CryptoVenue),
            ("fund", EconomyKind.Fund));
        captured.Sources![3].Reading!.TotalValueUsd.Should().Be(700m);
        captured.BankingTotal.Should().Be(100m);
        captured.BrokerageTotal.Should().Be(200m);
        captured.CryptoTotal.Should().Be(50m);
    }

    [Fact]
    public async Task Job_ReadsEachSourceOnce_SoRowsAndColumnsComeFromTheSameRead()
    {
        var reads = 0;
        var counting = new Mock<IWealthSource>();
        counting.SetupGet(s => s.SourceId).Returns("banking");
        counting.SetupGet(s => s.Kind).Returns(EconomyKind.Bank);
        counting.Setup(s => s.ReadAsync(UserId, It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(Reading(cash: ++reads * 100m)));
        NetWorthSnapshotData? captured = null;

        await JobOver(CapturingService(d => captured = d).Object, counting.Object).CaptureForUserAsync(UserId, Day);

        reads.Should().Be(1);
        captured!.BankingTotal.Should().Be(captured.Sources!.Single().Reading!.TotalValueUsd);
    }

    [Fact]
    public async Task Job_RecordsAThrowingSourceAsUnreadInsteadOfDroppingIt_AndCarriesItsSleeveForward()
    {
        NetWorthSnapshotData? captured = null;
        var job = JobOver(
            CapturingService(d => captured = d).Object,
            new FakeSource("banking", EconomyKind.Bank, Reading(cash: 100m)),
            new FakeSource("crypto", EconomyKind.CryptoVenue, reading: null));

        await job.CaptureForUserAsync(UserId, Day);

        captured!.Sources.Should().HaveCount(2);
        captured.Sources![1].SourceId.Should().Be("crypto");
        captured.Sources[1].Reading.Should().BeNull();
        captured.Sources[0].Reading!.CashUsd.Should().Be(100m, "one failing source does not stop the others");
        captured.CryptoFresh.Should().BeFalse("a sleeve that did not read is carried forward, not recorded as $0");
    }

    private static (Mock<INetWorthSnapshotRepository> Repo, Func<IReadOnlyList<NetWorthSnapshotSource>?> Rows) Repo(
        NetWorthSnapshot? previous = null)
    {
        IReadOnlyList<NetWorthSnapshotSource>? rows = null;
        var repo = new Mock<INetWorthSnapshotRepository>();
        repo.Setup(r => r.GetLatestBeforeUnscopedAsync(UserId, Day, It.IsAny<CancellationToken>())).ReturnsAsync(previous);
        repo.Setup(r => r.UpsertWithSourcesAsync(
                It.IsAny<NetWorthSnapshot>(), It.IsAny<IReadOnlyList<NetWorthSnapshotSource>>(), It.IsAny<CancellationToken>()))
            .Callback<NetWorthSnapshot, IReadOnlyList<NetWorthSnapshotSource>, CancellationToken>((_, r, _) => rows = r)
            .Returns(Task.CompletedTask);
        return (repo, () => rows);
    }

    private static NetWorthSnapshotData Data(params BookSourceReading[] figures) => new(
        Day, BankingTotal: 100m, BrokerageTotal: 210m, CryptoTotal: 50m, Sources: figures);

    [Fact]
    public async Task Service_WritesFiguresHealthAndKind_FromTheReading()
    {
        var (repo, rows) = Repo();

        await new NetWorthSnapshotService(repo.Object).PersistSnapshotAsync(UserId, Data(
            new("banking", EconomyKind.Bank, Reading(cash: 100m)),
            new("brokerage", EconomyKind.Brokerage, Reading(cash: 10m, invested: 200m)),
            new("crypto", EconomyKind.CryptoVenue, Reading(cash: 0m, invested: 50m))));

        var written = rows()!;
        written.Select(r => r.SourceId).Should().Equal("banking", "brokerage", "crypto");
        written[1].Kind.Should().Be(EconomyKind.Brokerage);
        written[1].Total.Should().Be(210m);
        written[1].Cash.Should().Be(10m);
        written[1].Invested.Should().Be(200m);
        written.Should().OnlyContain(r => r.Health == SourceHealth.Complete && r.Reasons.Length == 0);
        written.Sum(r => r.Total!.Value).Should().Be(360m, "the sources add up to the snapshot's sleeve totals");
    }

    [Theory]
    [InlineData(FxStatus.NearestPublished, SourceHealth.Complete, new string[0])]
    [InlineData(FxStatus.TodayRate, SourceHealth.Estimated, new[] { FxReasons.TodayRate })]
    [InlineData(FxStatus.SeedTable, SourceHealth.Estimated, new[] { FxReasons.TodayRate })]
    [InlineData(FxStatus.Unknown1To1, SourceHealth.Degraded, new[] { FxReasons.Unknown1To1 })]
    public async Task Service_MapsASourcesFxStatusToHealthAndReasons(FxStatus fx, SourceHealth health, string[] reasons)
    {
        var (repo, rows) = Repo();

        await new NetWorthSnapshotService(repo.Object).PersistSnapshotAsync(UserId, Data(
            new BookSourceReading("banking", EconomyKind.Bank, Reading(cash: 100m, fx: fx))));

        rows()!.Single().Health.Should().Be(health);
        rows()!.Single().Reasons.Should().Equal(reasons);
    }

    [Fact]
    public async Task Service_WritesAFailedSourceAsUnavailable_WithNullFiguresAndAStaleReason()
    {
        var (repo, rows) = Repo(new NetWorthSnapshot { BankingTotal = 100m, CryptoTotal = 45m });

        await new NetWorthSnapshotService(repo.Object).PersistSnapshotAsync(UserId, Data(
            new("banking", EconomyKind.Bank, Reading(cash: 100m)),
            new("crypto", EconomyKind.CryptoVenue, Reading: null)));

        var crypto = rows()!.Single(r => r.SourceId == "crypto");
        crypto.Health.Should().Be(SourceHealth.Unavailable);
        crypto.Reasons.Should().Equal(SourceReasons.StaleSource);
        crypto.Total.Should().BeNull("an unread source has no figure - null, never zero");
        crypto.Cash.Should().BeNull();
        crypto.Invested.Should().BeNull();
    }

    [Fact]
    public async Task Service_MarksASleeveTheSnapshotCarriedForward_AsDegradedAndStale()
    {
        var previous = new NetWorthSnapshot { BankingTotal = 100m, BrokerageTotal = 300m, CryptoTotal = 50m };
        var (repo, rows) = Repo(previous);

        await new NetWorthSnapshotService(repo.Object).PersistSnapshotAsync(UserId, new NetWorthSnapshotData(
            Day, BankingTotal: 100m, BrokerageTotal: 0m, CryptoTotal: 50m, BrokerageFresh: false,
            Sources:
            [
                new("banking", EconomyKind.Bank, Reading(cash: 100m)),
                new("brokerage", EconomyKind.Brokerage, Reading(cash: 0m)),
                new("crypto", EconomyKind.CryptoVenue, Reading(cash: 0m, invested: 50m, fx: FxStatus.TodayRate)),
            ]));

        var written = rows()!;
        written.Single(r => r.SourceId == "banking").Health.Should().Be(SourceHealth.Complete);
        var brokerage = written.Single(r => r.SourceId == "brokerage");
        brokerage.Health.Should().Be(SourceHealth.Degraded);
        brokerage.Reasons.Should().Equal(SourceReasons.StaleSource);
        written.Single(r => r.SourceId == "crypto").Health.Should().Be(SourceHealth.Estimated,
            "a source that was not carried forward keeps its own FX health");
    }

    [Fact]
    public async Task Service_WithNoSourceFigures_LeavesTheSourceRowsAlone()
    {
        var (repo, _) = Repo();
        repo.Setup(r => r.UpsertAsync(It.IsAny<NetWorthSnapshot>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await new NetWorthSnapshotService(repo.Object).PersistSnapshotAsync(
            UserId, new NetWorthSnapshotData(Day, 100m, 0m, 0m));

        repo.Verify(r => r.UpsertAsync(It.IsAny<NetWorthSnapshot>(), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.UpsertWithSourcesAsync(
            It.IsAny<NetWorthSnapshot>(), It.IsAny<IReadOnlyList<NetWorthSnapshotSource>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static async Task<(NetWorthSnapshot Snapshot, IReadOnlyList<NetWorthSnapshotSource> Rows)> CaptureThroughServiceAsync(
        DateTime bankingLastSync, NetWorthSnapshot? previous, params IWealthSource[] sources)
    {
        NetWorthSnapshot? snapshot = null;
        IReadOnlyList<NetWorthSnapshotSource>? rows = null;
        var repo = new Mock<INetWorthSnapshotRepository>();
        repo.Setup(r => r.GetLatestBeforeUnscopedAsync(UserId, Day, It.IsAny<CancellationToken>())).ReturnsAsync(previous);
        repo.Setup(r => r.UpsertWithSourcesAsync(
                It.IsAny<NetWorthSnapshot>(), It.IsAny<IReadOnlyList<NetWorthSnapshotSource>>(), It.IsAny<CancellationToken>()))
            .Callback<NetWorthSnapshot, IReadOnlyList<NetWorthSnapshotSource>, CancellationToken>((s, r, _) => (snapshot, rows) = (s, r))
            .Returns(Task.CompletedTask);

        await JobOver(new NetWorthSnapshotService(repo.Object), bankingLastSync, sources).CaptureForUserAsync(UserId, Day);

        return (snapshot!, rows!);
    }

    private static decimal ColumnOf(NetWorthSnapshot snapshot, string sourceId) => sourceId switch
    {
        "banking" => snapshot.BankingTotal,
        "brokerage" => snapshot.BrokerageTotal,
        _ => snapshot.CryptoTotal,
    };

    [Fact]
    public async Task EndToEnd_WhenEverySourceIsCompleteOrEstimated_TheRowsAddUpToTheSleeveColumns()
    {
        var (snapshot, rows) = await CaptureThroughServiceAsync(
            DateTime.UtcNow, previous: null,
            ThreeSources(crypto: Reading(cash: 0m, invested: 50m, fx: FxStatus.TodayRate)));

        rows.Should().OnlyContain(r => r.Health == SourceHealth.Complete || r.Health == SourceHealth.Estimated);
        rows.Sum(r => r.Total!.Value).Should().Be(snapshot.TotalNetWorth);
        foreach (var row in rows)
            row.Total.Should().Be(ColumnOf(snapshot, row.SourceId));
    }

    [Fact]
    public async Task EndToEnd_AStaleSleeveIsCarriedForward_AndItsRowIsNeitherCompleteNorEstimated()
    {
        var previous = new NetWorthSnapshot { BankingTotal = 500m, BrokerageTotal = 200m, CryptoTotal = 50m };

        var (snapshot, rows) = await CaptureThroughServiceAsync(
            DateTime.UtcNow.AddDays(-3), previous, ThreeSources());

        snapshot.BankingTotal.Should().Be(500m, "the stale sleeve carries yesterday's figure");
        snapshot.StaleSleeves.Should().Be("banking");
        var banking = rows.Single(r => r.SourceId == "banking");
        banking.Health.Should().Be(SourceHealth.Degraded);
        banking.Reasons.Should().Contain(SourceReasons.StaleSource);
        banking.Total.Should().Be(100m, "the row is what the source read, the column the carried figure");
        foreach (var row in rows.Where(r => r.Health is SourceHealth.Complete or SourceHealth.Estimated))
            row.Total.Should().Be(ColumnOf(snapshot, row.SourceId));
    }

    [Fact]
    public async Task EndToEnd_AFailedSource_IsUnavailableAndItsSleeveIsCarriedForward()
    {
        var previous = new NetWorthSnapshot { BankingTotal = 100m, BrokerageTotal = 200m, CryptoTotal = 45m };

        var (snapshot, rows) = await CaptureThroughServiceAsync(
            DateTime.UtcNow, previous,
            new FakeSource("banking", EconomyKind.Bank, Reading(cash: 100m)),
            new FakeSource("brokerage", EconomyKind.Brokerage, Reading(cash: 10m, invested: 190m)),
            new FakeSource("crypto", EconomyKind.CryptoVenue, reading: null));

        snapshot.CryptoTotal.Should().Be(45m);
        snapshot.StaleSleeves.Should().Be("crypto");
        var crypto = rows.Single(r => r.SourceId == "crypto");
        crypto.Health.Should().Be(SourceHealth.Unavailable);
        crypto.Total.Should().BeNull();
        foreach (var row in rows.Where(r => r.Health is SourceHealth.Complete or SourceHealth.Estimated))
            row.Total.Should().Be(ColumnOf(snapshot, row.SourceId));
    }

    [Theory]
    [InlineData("banking")]
    [InlineData("brokerage")]
    [InlineData("crypto")]
    public async Task EndToEnd_AFailedSleeveSourceOnTheFirstDay_FailsTheRunAndPersistsNothing(string failing)
    {
        var repo = new Mock<INetWorthSnapshotRepository>();
        repo.Setup(r => r.GetLatestBeforeUnscopedAsync(UserId, Day, It.IsAny<CancellationToken>())).ReturnsAsync((NetWorthSnapshot?)null);
        var sources = ThreeSources().Select(s => s.SourceId == failing
            ? new FakeSource(s.SourceId, s.Kind, reading: null)
            : s).ToArray();

        var run = () => JobOver(new NetWorthSnapshotService(repo.Object), sources).CaptureForUserAsync(UserId, Day);

        await run.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{failing}*");
        repo.Verify(r => r.UpsertAsync(It.IsAny<NetWorthSnapshot>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.UpsertWithSourcesAsync(
            It.IsAny<NetWorthSnapshot>(), It.IsAny<IReadOnlyList<NetWorthSnapshotSource>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EndToEnd_AFailedNonSleeveSourceOnTheFirstDay_StillWritesTheSnapshot()
    {
        var (snapshot, rows) = await CaptureThroughServiceAsync(
            DateTime.UtcNow, previous: null,
            [.. ThreeSources(), new FakeSource("fund", EconomyKind.Fund, reading: null)]);

        snapshot.TotalNetWorth.Should().Be(350m);
        rows.Single(r => r.SourceId == "fund").Health.Should().Be(SourceHealth.Unavailable);
    }
}
