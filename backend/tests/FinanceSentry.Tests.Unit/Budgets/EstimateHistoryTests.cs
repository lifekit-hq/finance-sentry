namespace FinanceSentry.Tests.Unit.Budgets;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Budgets.Application.Estimates;
using Xunit;

/// <summary>
/// Which months an estimate learns from: the lookback, the configured exclusion window, the coverage-hole rule (a
/// month a hole touches is skipped, never counted as zero) and "no data before the first transaction".
/// </summary>
public sealed class EstimateHistoryTests
{
    private static readonly DateOnly Jan2020 = new(2020, 1, 1);
    private static readonly Guid Account = Guid.NewGuid();

    // Nothing is excluded unless a test configures it, so a month list means what it says.
    private static BudgetEstimateOptions NoExclusions => new();

    private static EstimateHistory Resolve(
        DateOnly month,
        DateOnly currentMonth,
        DateOnly? firstDataDay = null,
        BudgetEstimateOptions? options = null,
        params CoverageHoleSpan[] holes)
        => EstimateHistory.Resolve(
            month, currentMonth, firstDataDay ?? Jan2020, options ?? NoExclusions, holes);

    [Fact]
    public void Window_IsTheSixCompleteMonthsBeforeTheCurrentOne_OldestFirst()
    {
        var history = Resolve(new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 1));

        Assert.Equal(["2026-09", "2026-10", "2026-11", "2026-12", "2027-01", "2027-02"], history.UsedMonths);
    }

    [Fact]
    public void Window_OfAPastMonth_EndsBeforeThatMonth()
    {
        var history = Resolve(new DateOnly(2027, 1, 1), new DateOnly(2027, 6, 1));

        Assert.Equal(["2026-07", "2026-08", "2026-09", "2026-10", "2026-11", "2026-12"], history.UsedMonths);
    }

    [Fact]
    public void Window_OfAFutureMonth_StopsAtTheLastCompleteMonth()
    {
        var history = Resolve(new DateOnly(2027, 9, 1), new DateOnly(2027, 3, 1));

        Assert.Equal("2027-02", history.UsedMonths[^1]);
    }

    [Fact]
    public void Default_ExcludesNothing_SoSeptemberAndOctober2026AreUsedForAnyone()
    {
        var history = Resolve(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1), options: new BudgetEstimateOptions());

        Assert.Equal(["2026-05", "2026-06", "2026-07", "2026-08", "2026-09", "2026-10"], history.UsedMonths);
        Assert.Empty(history.Skipped);
    }

    [Fact]
    public void ConfiguredExclusion_LeavesOutOnlyThoseMonths()
    {
        var options = new BudgetEstimateOptions { ExcludedMonths = ["2026-07"] };

        var history = Resolve(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1), options: options);

        Assert.Equal(["2026-05", "2026-06", "2026-08", "2026-09", "2026-10"], history.UsedMonths);
        Assert.Equal([new SkippedMonth("2026-07", SkipReasons.Excluded)], history.Skipped);
    }

    [Fact]
    public void CoverageHole_SkipsEveryMonthItTouches_NotJustTheOnesItCovers()
    {
        // A hole from 20 Jan to 3 Feb touches January and February.
        var hole = new CoverageHoleSpan(Account, new DateOnly(2027, 1, 20), new DateOnly(2027, 2, 3));

        var history = Resolve(new DateOnly(2027, 4, 1), new DateOnly(2027, 4, 1), holes: hole);

        Assert.Equal(["2026-10", "2026-11", "2026-12", "2027-03"], history.UsedMonths);
        Assert.Equal(
            [new SkippedMonth("2027-01", SkipReasons.CoverageGap), new SkippedMonth("2027-02", SkipReasons.CoverageGap)],
            history.Skipped);
    }

    [Fact]
    public void OpenCoverageHole_SkipsEveryMonthFromItsStart()
    {
        var hole = new CoverageHoleSpan(Account, new DateOnly(2027, 2, 8), null);

        var history = Resolve(new DateOnly(2027, 4, 1), new DateOnly(2027, 4, 1), holes: hole);

        Assert.Equal(["2026-10", "2026-11", "2026-12", "2027-01"], history.UsedMonths);
        Assert.Equal(
            ["2027-02", "2027-03"], history.Skipped.Select(s => s.Month));
    }

    [Fact]
    public void ExcludedMonth_InsideAHole_IsReportedAsExcluded_NotTwice()
    {
        var hole = new CoverageHoleSpan(Account, new DateOnly(2026, 9, 7), null);

        var history = Resolve(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1), options: new BudgetEstimateOptions { ExcludedMonths = ["2026-09", "2026-10"] }, holes: hole);

        Assert.Equal(
            [new SkippedMonth("2026-09", SkipReasons.Excluded), new SkippedMonth("2026-10", SkipReasons.Excluded)],
            history.Skipped);
    }

    [Fact]
    public void MonthsBeforeTheFirstData_AreNoData_NotZero_AndAreNotReported()
    {
        // First transaction on 14 Jan 2027: January itself counts (as the spike sentinel's observed months do);
        // the months before it are not in the window at all.
        var history = Resolve(new DateOnly(2027, 4, 1), new DateOnly(2027, 4, 1), firstDataDay: new DateOnly(2027, 1, 14));

        Assert.Equal(["2027-01", "2027-02", "2027-03"], history.UsedMonths);
        Assert.Empty(history.Skipped);
    }

    [Fact]
    public void NoTransactionsAtAll_HasNoHistory()
    {
        var history = EstimateHistory.Resolve(
            new DateOnly(2027, 4, 1), new DateOnly(2027, 4, 1), null, new BudgetEstimateOptions(), []);

        Assert.Empty(history.UsedMonths);
        Assert.False(history.IsEnough);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(6, true)]
    public void History_IsEnoughFromThreeUsableMonths(int usable, bool enough)
    {
        // The first usable month sits `usable` months back from April 2027.
        var firstData = new DateOnly(2027, 4, 1).AddMonths(-usable);

        var history = Resolve(new DateOnly(2027, 4, 1), new DateOnly(2027, 4, 1), firstDataDay: firstData);

        Assert.Equal(usable, history.UsedMonths.Count);
        Assert.Equal(enough, history.IsEnough);
    }

    [Fact]
    public void HolesAndExclusions_CanLeaveLessThanThreeMonths_SoNoSuggestionIsMade()
    {
        var hole = new CoverageHoleSpan(Account, new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 31));

        var history = Resolve(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1), options: new BudgetEstimateOptions { ExcludedMonths = ["2026-09", "2026-10"] }, holes: hole);

        Assert.Equal(["2026-05", "2026-08"], history.UsedMonths);
        Assert.False(history.IsEnough);
    }
}
