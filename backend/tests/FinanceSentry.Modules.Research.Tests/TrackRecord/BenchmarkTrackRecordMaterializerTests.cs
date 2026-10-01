namespace FinanceSentry.Modules.Research.Tests.TrackRecord;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

public class BenchmarkTrackRecordMaterializerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AsOf = new(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IThesisEventRepository> events = new();
    private readonly Mock<IBookFiguresService> bookFigures = new();
    private readonly Mock<IBrokerageHoldingsReader> brokerage = new();
    private readonly Mock<IAlertGeneratorService> alerts = new();
    private readonly InMemoryRecordRepository records = new();
    private readonly List<ThesisEvent> series = [];

    public BenchmarkTrackRecordMaterializerTests()
    {
        events.Setup(r => r.ListUnscopedAsync(UserId, null, It.IsAny<CancellationToken>())).ReturnsAsync(() => series);
        bookFigures.Setup(b => b.ReadAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(Figures());
        brokerage.Setup(b => b.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
    }

    private BenchmarkTrackRecordMaterializer Sut() => new(
        events.Object,
        records,
        bookFigures.Object,
        brokerage.Object,
        alerts.Object,
        new BenchmarkRelativeCalculator(new ThesisPerformanceCalculator()),
        Options.Create(new FrictionConfig()),
        Options.Create(new RelativePerformanceConfig()),
        new FixedClock(Now),
        NullLogger<BenchmarkTrackRecordMaterializer>.Instance);

    private Guid AddSeries(string ticker, decimal fromPrice, decimal toPrice, int fromDaysAgo = 100)
    {
        var id = Guid.NewGuid();
        series.Add(Event(id, ticker, ThesisEventType.Created, fromDaysAgo, fromPrice, 500m));
        series.Add(Event(id, ticker, ThesisEventType.Snapshot, 1, toPrice, 500m));
        return id;
    }

    private static ThesisEvent Event(
        Guid subjectId, string ticker, ThesisEventType type, int daysAgo, decimal? subject, decimal? benchmark)
        => new()
        {
            UserId = UserId,
            SubjectType = ThesisSubjectType.Thesis,
            SubjectId = subjectId,
            Ticker = ticker,
            EventType = type,
            Timestamp = Now.AddDays(-daysAgo),
            SubjectPrice = subject,
            BenchmarkPrice = benchmark,
        };

    private static BookFigures Figures(IReadOnlyList<BookFigurePosition>? positions = null, params string[] stale)
        => new(0m, 0m, 0m, 0m, 0m, positions ?? [], stale.Length > 0, stale);

    private static BookFigurePosition Position(string symbol, string assetClass, decimal usdValue)
        => new(symbol, assetClass, 1m, 100m, usdValue, "ibkr");

    private static BrokerageHoldingSummary Holding(string symbol, string basisState, decimal quantity = 1m)
        => new(symbol, "STK", quantity, 100m, DateTime.UtcNow, "ibkr", 100m, basisState);

    private BenchmarkRelativeRecord Stored(TrackRecordScope scope, string key, TrackRecordWindow window)
        => records.Runs[AsOf].Single(r => r.Scope == scope && r.ScopeKey == key && r.Window == window);

    [Fact]
    public async Task NoThesisSeries_WritesNothing()
    {
        series.Add(new ThesisEvent { UserId = UserId, SubjectType = ThesisSubjectType.Candidate, Ticker = "MU" });

        await Sut().MaterializeAsync(UserId);

        records.Runs.Should().BeEmpty();
        bookFigures.Verify(b => b.ReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StaleHoldings_SkipTheRun_AndTouchNoAlerts()
    {
        AddSeries("MU", 100m, 90m);
        bookFigures.Setup(b => b.ReadAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Figures(stale: "brokerage"));

        await Sut().MaterializeAsync(UserId);

        records.Runs.Should().BeEmpty();
        alerts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PlacesThesesInSleeves_AndGatesNetOnTheVerifiedBasis()
    {
        var mu = AddSeries("MU", 100m, 120m);
        var nvda = AddSeries("NVDA", 100m, 120m);
        var tsla = AddSeries("TSLA", 100m, 120m);
        bookFigures.Setup(b => b.ReadAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(Figures(
        [
            Position("MU", "Equity", 1_000m),
            Position("NVDA", "Crypto", 10m),
            Position("NVDA", "Equity", 500m),
        ]));
        brokerage.Setup(b => b.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            Holding("MU", "Verified"),
            Holding("NVDA", "Unverified"),
        ]);

        await Sut().MaterializeAsync(UserId);

        var muRow = Stored(TrackRecordScope.Thesis, mu.ToString("N"), TrackRecordWindow.SinceInception);
        muRow.NetGate.Should().Be(NetExcessGate.Verified);
        muRow.NetExcessReturnPct.Should().NotBeNull();

        var nvdaRow = Stored(TrackRecordScope.Thesis, nvda.ToString("N"), TrackRecordWindow.SinceInception);
        nvdaRow.NetGate.Should().Be(NetExcessGate.Unverified);
        nvdaRow.NetExcessReturnPct.Should().BeNull();

        var tslaRow = Stored(TrackRecordScope.Thesis, tsla.ToString("N"), TrackRecordWindow.SinceInception);
        tslaRow.NetGate.Should().Be(NetExcessGate.NotHeld);

        Stored(TrackRecordScope.Sleeve, "Equity", TrackRecordWindow.SinceInception).ConstituentCount.Should().Be(2);
        Stored(TrackRecordScope.Sleeve, BenchmarkRelativeCalculator.UnheldSleeve, TrackRecordWindow.SinceInception)
            .ConstituentCount.Should().Be(1);
        records.Runs[AsOf].Should().NotContain(r => r.Scope == TrackRecordScope.Sleeve && r.ScopeKey == "Crypto");
    }

    [Fact]
    public async Task VerifiedBasisWithZeroQuantity_DoesNotReleaseNet()
    {
        var mu = AddSeries("MU", 100m, 120m);
        bookFigures.Setup(b => b.ReadAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Figures([Position("MU", "Equity", 1_000m)]));
        brokerage.Setup(b => b.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Holding("MU", "Verified", quantity: 0m)]);

        await Sut().MaterializeAsync(UserId);

        Stored(TrackRecordScope.Thesis, mu.ToString("N"), TrackRecordWindow.SinceInception)
            .NetGate.Should().Be(NetExcessGate.Unverified);
    }

    [Fact]
    public async Task BrokerageReadFailure_VerifiesNothing()
    {
        var mu = AddSeries("MU", 100m, 120m);
        bookFigures.Setup(b => b.ReadAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Figures([Position("MU", "Equity", 1_000m)]));
        brokerage.Setup(b => b.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("down"));

        await Sut().MaterializeAsync(UserId);

        var row = Stored(TrackRecordScope.Thesis, mu.ToString("N"), TrackRecordWindow.SinceInception);
        row.NetGate.Should().Be(NetExcessGate.Unverified);
        row.NetExcessReturnPct.Should().BeNull();
    }

    [Fact]
    public async Task SkipsPendingAndForeignBenchmarkPoints()
    {
        var mu = AddSeries("MU", 100m, 120m);
        series.Add(Event(mu, "MU", ThesisEventType.Snapshot, 0, 500m, 500m));
        series[^1].PricesPending = true;
        series.Add(Event(mu, "MU", ThesisEventType.Snapshot, 0, 900m, 500m));
        series[^1].BenchmarkTicker = "QQQ";

        await Sut().MaterializeAsync(UserId);

        Stored(TrackRecordScope.Thesis, mu.ToString("N"), TrackRecordWindow.SinceInception)
            .SubjectReturnPct.Should().Be(20m);
    }

    [Fact]
    public async Task SustainedBookUnderperformance_RaisesTheAlert_RecoveredAndVanishedScopesResolve()
    {
        AddSeries("MU", 100m, 90m);
        bookFigures.Setup(b => b.ReadAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Figures([Position("MU", "Equity", 1_000m)]));
        var previousAsOf = AsOf.AddDays(-7);
        records.Runs[previousAsOf] =
        [
            Previous(previousAsOf, TrackRecordScope.Book, "book", runs: 3),
            Previous(previousAsOf, TrackRecordScope.Sleeve, "Crypto", runs: 4),
        ];

        await Sut().MaterializeAsync(UserId);

        var book = Stored(TrackRecordScope.Book, "book", TrackRecordWindow.ThreeMonths);
        book.ExcessReturnPct.Should().Be(-10m);
        book.UnderperformingRuns.Should().Be(4);
        book.SustainedUnderperformance.Should().BeTrue();

        alerts.Verify(a => a.GenerateRelativeUnderperformanceAlertAsync(
            UserId, "Book", "book", "Book", "ThreeMonths", "SPY", -10m, 4, 5m, It.IsAny<CancellationToken>()), Times.Once);
        alerts.Verify(a => a.ResolveRelativeUnderperformanceAlertAsync(
            UserId, "Sleeve", "Equity", It.IsAny<CancellationToken>()), Times.Once);
        alerts.Verify(a => a.ResolveRelativeUnderperformanceAlertAsync(
            UserId, "Sleeve", "Crypto", It.IsAny<CancellationToken>()), Times.Once);
        alerts.VerifyNoOtherCalls();
    }

    private static BenchmarkRelativeRecord Previous(DateTimeOffset asOf, TrackRecordScope scope, string key, int runs)
        => new()
        {
            UserId = UserId,
            AsOf = asOf,
            Scope = scope,
            ScopeKey = key,
            Label = key,
            Window = TrackRecordWindow.ThreeMonths,
            Covered = true,
            ExcessReturnPct = -8m,
            UnderperformingRuns = runs,
        };

    private sealed class InMemoryRecordRepository : IBenchmarkRelativeRecordRepository
    {
        public SortedDictionary<DateTimeOffset, IReadOnlyList<BenchmarkRelativeRecord>> Runs { get; } = [];

        public Task ReplaceRunAsync(
            Guid userId, DateTimeOffset asOf, IReadOnlyList<BenchmarkRelativeRecord> rows, CancellationToken ct = default)
        {
            Runs[asOf] = rows;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<BenchmarkRelativeRecord>> ListPreviousRunUnscopedAsync(
            Guid userId, DateTimeOffset asOf, CancellationToken ct = default)
            => Task.FromResult(Runs.LastOrDefault(r => r.Key < asOf).Value ?? []);

        public Task<IReadOnlyList<BenchmarkRelativeRecord>> ListLatestRunAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(Runs.Count == 0 ? [] : Runs.Last().Value);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
