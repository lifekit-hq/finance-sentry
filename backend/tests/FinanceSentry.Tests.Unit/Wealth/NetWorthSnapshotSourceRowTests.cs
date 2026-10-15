namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Wealth.Application.Services;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using FinanceSentry.Modules.Wealth.Infrastructure.Jobs;
using FluentAssertions;
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

    private sealed class FakeSource(string id, EconomyKind kind, WealthSourceReading? reading) : IWealthSource
    {
        public string SourceId => id;

        public EconomyKind Kind => kind;

        public Task<WealthSourceReading> ReadAsync(Guid userId, CancellationToken ct = default)
            => reading is null
                ? throw new InvalidOperationException("provider down")
                : Task.FromResult(reading);
    }

    private static WealthSourceReading Reading(decimal cash, decimal invested = 0m, FxStatus fx = FxStatus.Exact)
        => new(cash, invested == 0m ? [] : [new BookFigurePosition("AAPL", "equity", 1m, null, invested, "ibkr")], fx);

    private static NetWorthSnapshotJob JobOver(
        Mock<INetWorthSnapshotService> service, params IWealthSource[] sources)
    {
        var banking = new Mock<IBankingTotalsReader>();
        banking.Setup(b => b.GetTotalUsdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(0m);
        var crypto = new Mock<ICryptoHoldingsReader>();
        crypto.Setup(c => c.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var brokerage = new Mock<IBrokerageHoldingsReader>();
        brokerage.Setup(b => b.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        return new NetWorthSnapshotJob(
            banking.Object, crypto.Object, brokerage.Object, service.Object,
            Mock.Of<IBookFiguresService>(b => b.ReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()) == Task.FromResult(BookFigures.Empty)),
            sources);
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

    [Fact]
    public async Task Job_HandsOneFigurePerRegisteredSource_InRegistrationOrder_WithNoChangeForAFourthSource()
    {
        NetWorthSnapshotData? captured = null;
        var job = JobOver(
            CapturingService(d => captured = d),
            new FakeSource("banking", EconomyKind.Bank, Reading(cash: 100m)),
            new FakeSource("brokerage", EconomyKind.Brokerage, Reading(cash: 10m, invested: 200m)),
            new FakeSource("crypto", EconomyKind.CryptoVenue, Reading(cash: 0m, invested: 50m)),
            new FakeSource("fund", EconomyKind.Fund, Reading(cash: 0m, invested: 700m)));

        await job.CaptureForUserAsync(UserId, Day);

        captured!.Sources!.Select(f => (f.SourceId, f.Kind)).Should().Equal(
            ("banking", EconomyKind.Bank),
            ("brokerage", EconomyKind.Brokerage),
            ("crypto", EconomyKind.CryptoVenue),
            ("fund", EconomyKind.Fund));
        captured.Sources![3].Reading!.TotalValueUsd.Should().Be(700m);
    }

    [Fact]
    public async Task Job_RecordsAThrowingSourceAsUnreadInsteadOfDroppingIt()
    {
        NetWorthSnapshotData? captured = null;
        var job = JobOver(
            CapturingService(d => captured = d),
            new FakeSource("banking", EconomyKind.Bank, Reading(cash: 100m)),
            new FakeSource("crypto", EconomyKind.CryptoVenue, reading: null));

        await job.CaptureForUserAsync(UserId, Day);

        captured!.Sources.Should().HaveCount(2);
        captured.Sources![1].SourceId.Should().Be("crypto");
        captured.Sources[1].Reading.Should().BeNull();
        captured.Sources[0].Reading!.CashUsd.Should().Be(100m, "one failing source does not stop the others");
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

    private static NetWorthSnapshotData Data(params SourceSnapshotFigure[] figures) => new(
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
            new SourceSnapshotFigure("banking", EconomyKind.Bank, Reading(cash: 100m, fx: fx))));

        rows()!.Single().Health.Should().Be(health);
        rows()!.Single().Reasons.Should().Equal(reasons);
    }

    [Fact]
    public async Task Service_WritesAFailedSourceAsUnavailable_WithNullFiguresAndAStaleReason()
    {
        var (repo, rows) = Repo();

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
}
