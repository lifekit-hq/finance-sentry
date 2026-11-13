using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.MarketStructure;
using FinanceSentry.Modules.Radar.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Modules.Radar.Tests.MarketStructure;

/// <summary>#558 stage 2 / 022 FR-001d: the 63-day return correlation maths and its reader, on fixed series.</summary>
public sealed class ReturnCorrelationTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);

    /// <summary>A deterministic, zero-ish-mean return pattern that is not constant (period-7 sawtooth with a drift term).</summary>
    private static decimal[] Pattern(int count, int phase = 0)
        => Enumerable.Range(0, count).Select(i => ((i + phase) * 3 % 7 - 3) / 100m).ToArray();

    private static Dictionary<DateOnly, decimal> Returns(IReadOnlyList<decimal> values, DateOnly start)
        => values.Select((v, i) => (Date: start.AddDays(i), Value: v)).ToDictionary(x => x.Date, x => x.Value);

    private static Dictionary<DateOnly, decimal> Closes(IReadOnlyList<decimal> returns, DateOnly firstDate)
    {
        var closes = new Dictionary<DateOnly, decimal> { [firstDate] = 100m };
        var price = 100m;
        for (var i = 0; i < returns.Count; i++)
        {
            price *= 1m + returns[i];
            closes[firstDate.AddDays(i + 1)] = price;
        }

        return closes;
    }

    [Fact]
    public void DailyReturns_AreCloseToCloseKeyedByLaterDate_SkippingNonPositiveCloses()
    {
        var closes = new Dictionary<DateOnly, decimal>
        {
            [Start] = 100m, [Start.AddDays(1)] = 110m, [Start.AddDays(2)] = 0m, [Start.AddDays(3)] = 121m,
        };

        var returns = ReturnCorrelation.DailyReturns(closes);

        returns.Should().HaveCount(2);
        returns[Start.AddDays(1)].Should().Be(0.10m);
        returns[Start.AddDays(3)].Should().Be(121m / 110m - 1m);
    }

    [Fact]
    public void Pearson_IdenticalSeries_IsOne_AndMirroredSeries_IsMinusOne()
    {
        var x = Returns(Pattern(63), Start);
        var mirrored = Returns(Pattern(63).Select(v => -v).ToArray(), Start);

        ReturnCorrelation.Pearson(x, x).Value.Should().Be(1m);
        ReturnCorrelation.Pearson(x, mirrored).Value.Should().Be(-1m);
        ReturnCorrelation.Pearson(x, x).Observations.Should().Be(63);
    }

    [Fact]
    public void Pearson_ScaledSeries_IsOne_BecauseCorrelationIgnoresMagnitude()
    {
        var x = Returns(Pattern(63), Start);
        var scaled = Returns(Pattern(63).Select(v => v * 2.5m + 0.001m).ToArray(), Start);

        ReturnCorrelation.Pearson(x, scaled).Value.Should().Be(1m);
    }

    [Fact]
    public void Pearson_KnownValue_ForHandComputedSeries()
    {
        // x = [1,2,3,4,5] repeated; y = x + a zero-mean offset orthogonal to x per block is awkward, so use the
        // textbook pair x=(1,2,3), y=(1,3,2): r = 0.5. Repeating the block keeps r = 0.5 (same per-block moments).
        var xs = Enumerable.Repeat(new[] { 1m, 2m, 3m }, 20).SelectMany(b => b).Select(v => v / 100m).ToArray();
        var ys = Enumerable.Repeat(new[] { 1m, 3m, 2m }, 20).SelectMany(b => b).Select(v => v / 100m).ToArray();

        var outcome = ReturnCorrelation.Pearson(Returns(xs, Start), Returns(ys, Start));

        outcome.Value.Should().Be(0.5m);
        outcome.Observations.Should().Be(60);
    }

    [Fact]
    public void Pearson_UsesOnlyTheLast63CommonReturns()
    {
        // 100 common days: the first 37 are mirrored, the last 63 identical. A full-history correlation would
        // be dragged below 1; the 63-day window must see only the identical tail.
        var head = Pattern(37);
        var tail = Pattern(63, phase: 5);
        var x = Returns(head.Concat(tail).ToArray(), Start);
        var y = Returns(head.Select(v => -v).Concat(tail).ToArray(), Start);

        var outcome = ReturnCorrelation.Pearson(x, y);

        outcome.Value.Should().Be(1m);
        outcome.Observations.Should().Be(ReturnCorrelation.WindowReturns);
    }

    [Fact]
    public void Pearson_AlignsOnCommonDates_NotPositions()
    {
        var x = Returns(Pattern(80), Start);
        var y = Returns(Pattern(80), Start.AddDays(10)); // same values shifted 10 days: only 70 dates overlap, misaligned values

        var outcome = ReturnCorrelation.Pearson(x, y);

        outcome.Observations.Should().Be(63);
        outcome.Value.Should().NotBe(1m);
    }

    [Fact]
    public void Pearson_FewerThanMinObservations_IsNotEvaluable_WithObservationCount()
    {
        var x = Returns(Pattern(ReturnCorrelation.MinObservations - 1), Start);

        var outcome = ReturnCorrelation.Pearson(x, x);

        outcome.Value.Should().BeNull();
        outcome.Reason.Should().Be(CorrelationReasons.InsufficientOverlap);
        outcome.Observations.Should().Be(ReturnCorrelation.MinObservations - 1);
    }

    [Fact]
    public void Pearson_ExactlyMinObservations_IsEvaluable()
    {
        var x = Returns(Pattern(ReturnCorrelation.MinObservations), Start);

        ReturnCorrelation.Pearson(x, x).Value.Should().Be(1m);
    }

    [Fact]
    public void Pearson_FlatSeries_IsNotEvaluable_ZeroVariance()
    {
        var x = Returns(Pattern(63), Start);
        var flat = Returns(Enumerable.Repeat(0m, 63).ToArray(), Start);

        var outcome = ReturnCorrelation.Pearson(x, flat);

        outcome.Value.Should().BeNull();
        outcome.Reason.Should().Be(CorrelationReasons.ZeroVariance);
    }

    [Fact]
    public void Pearson_EmptySeries_IsNotEvaluable_NoBars()
    {
        var x = Returns(Pattern(63), Start);

        var outcome = ReturnCorrelation.Pearson(x, new Dictionary<DateOnly, decimal>());

        outcome.Value.Should().BeNull();
        outcome.Reason.Should().Be(CorrelationReasons.NoBars);
    }

    [Fact]
    public void WeightedBookReturns_AveragesByWeight_AndRenormalisesOverPositionsPresentThatDate()
    {
        var a = new Dictionary<DateOnly, decimal> { [Start] = 0.04m, [Start.AddDays(1)] = 0.02m };
        var b = new Dictionary<DateOnly, decimal> { [Start] = -0.04m };

        var book = ReturnCorrelation.WeightedBookReturns([(a, 3m), (b, 1m)]);

        book[Start].Should().Be((0.04m * 3m - 0.04m) / 4m);
        book[Start.AddDays(1)].Should().Be(0.02m); // only A has a return that day
    }

    // ── the reader over stored bars ──

    private static DailyBar[] Bars(string ticker, IReadOnlyList<decimal> returns, int barCount)
    {
        // Last bar is yesterday so every bar sits inside the reader's lookback regardless of when the suite runs.
        var first = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-barCount);
        return Closes(returns, first)
            .Select(kv => new DailyBar { Ticker = ticker, Date = kv.Key, AdjClose = kv.Value, Close = kv.Value })
            .ToArray();
    }

    private static ReturnCorrelationReader ReaderOver(params DailyBar[][] series)
        => new(new FakeBars(series.SelectMany(s => s).GroupBy(b => b.Ticker).ToDictionary(g => g.Key, g => (IReadOnlyList<DailyBar>)g.OrderBy(b => b.Date).ToList())));

    [Fact]
    public async Task Reader_ReportsEachTopHolding_AndWeighsTheBookByPositionValue()
    {
        var x = Pattern(70);
        var reader = ReaderOver(
            Bars("NEW", x, 70),
            Bars("AAA", x, 70),
            Bars("BBB", x.Select(v => -v).ToArray(), 70));

        var result = await reader.GetAsync(
            "new", [new("AAA", 3_000m), new("BBB", 1_000m)], topHoldings: 5, minBookCoverage: 0.5m);

        result.Ticker.Should().Be("NEW");
        result.WindowDays.Should().Be(63);
        result.TopHoldings.Select(h => (h.Symbol, h.Correlation)).Should().Equal(("AAA", 1m), ("BBB", -1m));
        result.TopHoldings[0].Weight.Should().Be(0.75m);
        result.TopHoldings[0].Observations.Should().Be(63);
        // book = (3x - x)/4 = 0.5x -> moves with the ticker
        result.Book.Correlation.Should().Be(1m);
        result.Book.Coverage.Should().Be(1m);
        result.Book.NotEvaluableReason.Should().BeNull();
    }

    [Fact]
    public async Task Reader_KeepsOnlyTheLargestNHoldings_ByValue_ButScoresTheWholeBook()
    {
        var x = Pattern(70);
        var reader = ReaderOver(
            Bars("NEW", x, 70), Bars("SMALL", x, 70), Bars("BIG", x, 70), Bars("MID", x, 70));

        var result = await reader.GetAsync(
            "NEW", [new("SMALL", 1m), new("BIG", 9m), new("MID", 5m)], topHoldings: 2, minBookCoverage: 0.5m);

        result.TopHoldings.Select(h => h.Symbol).Should().Equal("BIG", "MID");
        result.Book.Coverage.Should().Be(1m);
    }

    [Fact]
    public async Task Reader_ExcludesTheTickerItself_FromTopHoldingsAndTheBook()
    {
        var x = Pattern(70);
        var reader = ReaderOver(Bars("NEW", x, 70), Bars("AAA", x.Select(v => -v).ToArray(), 70));

        var result = await reader.GetAsync("NEW", [new("NEW", 9_000m), new("AAA", 1_000m)], 5, 0.5m);

        result.TopHoldings.Should().ContainSingle().Which.Symbol.Should().Be("AAA");
        result.Book.Correlation.Should().Be(-1m); // not diluted by the ticker's own position
    }

    [Fact]
    public async Task Reader_HoldingWithoutBars_IsNotEvaluable_NoBars_AndLowersBookCoverage()
    {
        var x = Pattern(70);
        var reader = ReaderOver(Bars("NEW", x, 70), Bars("AAA", x, 70));

        var result = await reader.GetAsync("NEW", [new("AAA", 5_000m), new("GHOST", 5_000m)], 5, 0.5m);

        var ghost = result.TopHoldings.Single(h => h.Symbol == "GHOST");
        ghost.Correlation.Should().BeNull();
        ghost.NotEvaluableReason.Should().Be(CorrelationReasons.NoBars);
        result.Book.Coverage.Should().Be(0.5m);
        result.Book.Correlation.Should().Be(1m);
    }

    [Fact]
    public async Task Reader_BookCoverageBelowMinimum_BookIsNotEvaluable_ButHoldingsStillReport()
    {
        var x = Pattern(70);
        var reader = ReaderOver(Bars("NEW", x, 70), Bars("AAA", x, 70));

        var result = await reader.GetAsync("NEW", [new("AAA", 4_000m), new("GHOST", 6_000m)], 5, 0.5m);

        result.Book.Correlation.Should().BeNull();
        result.Book.NotEvaluableReason.Should().Be(CorrelationReasons.BookCoverageTooLow);
        result.Book.Coverage.Should().Be(0.4m);
        result.TopHoldings.Single(h => h.Symbol == "AAA").Correlation.Should().Be(1m);
    }

    [Fact]
    public async Task Reader_TickerWithShortHistory_IsNotEvaluableAgainstEveryReference()
    {
        var x = Pattern(70);
        var reader = ReaderOver(Bars("NEW", Pattern(20), 20), Bars("AAA", x, 70));

        var result = await reader.GetAsync("NEW", [new("AAA", 1_000m)], 5, 0.5m);

        var aaa = result.TopHoldings.Single();
        aaa.Correlation.Should().BeNull();
        aaa.NotEvaluableReason.Should().Be(CorrelationReasons.InsufficientOverlap);
        aaa.Observations.Should().Be(20);
        result.Book.Correlation.Should().BeNull();
        result.Book.NotEvaluableReason.Should().Be(CorrelationReasons.InsufficientOverlap);
    }

    [Fact]
    public async Task Reader_TickerWithNoBars_IsNotEvaluableEverywhere()
    {
        var reader = ReaderOver(Bars("AAA", Pattern(70), 70));

        var result = await reader.GetAsync("NOPE", [new("AAA", 1_000m)], 5, 0.5m);

        result.TopHoldings.Single().NotEvaluableReason.Should().Be(CorrelationReasons.NoBars);
        result.Book.NotEvaluableReason.Should().Be(CorrelationReasons.NoBars);
        result.Book.Correlation.Should().BeNull();
    }

    [Fact]
    public async Task Reader_EmptyBook_IsNotEvaluable_EmptyBook()
    {
        var reader = ReaderOver(Bars("NEW", Pattern(70), 70));

        var result = await reader.GetAsync("NEW", [], 5, 0.5m);

        result.TopHoldings.Should().BeEmpty();
        result.Book.Correlation.Should().BeNull();
        result.Book.NotEvaluableReason.Should().Be(CorrelationReasons.EmptyBook);
    }

    [Fact]
    public async Task Reader_MergesDuplicateSymbolsAcrossAccounts_AndIgnoresNonPositiveValues()
    {
        var x = Pattern(70);
        var reader = ReaderOver(Bars("NEW", x, 70), Bars("AAA", x, 70), Bars("BBB", x, 70));

        var result = await reader.GetAsync(
            "NEW", [new("AAA", 1_000m), new("aaa", 1_000m), new("BBB", 0m), new("CCC", -5m)], 5, 0.5m);

        result.TopHoldings.Should().ContainSingle().Which.Weight.Should().Be(1m);
    }

    private sealed class FakeBars(IReadOnlyDictionary<string, IReadOnlyList<DailyBar>> byTicker) : IDailyBarRepository
    {
        public Task<int> UpsertRangeAsync(IReadOnlyCollection<DailyBar> bars, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<DailyBar>> GetSinceAsync(string ticker, DateOnly since, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DailyBar>>(
                byTicker.TryGetValue(ticker, out var series) ? series.Where(b => b.Date >= since).ToList() : []);

        public Task<DateOnly?> GetLatestDateAsync(string ticker, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, DateOnly>> GetLatestDatesAsync(IReadOnlyCollection<string> tickers, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
