namespace FinanceSentry.Tests.Unit.BankSync.Infrastructure;

using FinanceSentry.Modules.BankSync.Application.Services;
using Xunit;

/// <summary>Edges of the coverage sentinel's rule: the 20% line, the 2-week run, the history and size floors.</summary>
public class CoverageGapDetectorTests
{
    private static readonly DateOnly First = new(2026, 5, 4); // a Monday
    private const decimal Usual = 100m;

    /// <summary>Weeks from <see cref="First"/> up to (not including) <paramref name="quietFrom"/> at <see cref="Usual"/>, then the given quiet weeks.</summary>
    private static Dictionary<DateOnly, decimal> Weeks(DateOnly quietFrom, params decimal[] quiet)
    {
        var weeks = new Dictionary<DateOnly, decimal>();
        for (var w = First; w < quietFrom; w = w.AddDays(7))
            weeks[w] = Usual;
        for (var i = 0; i < quiet.Length; i++)
            weeks[quietFrom.AddDays(7 * i)] = quiet[i];
        return weeks;
    }

    private static readonly DateOnly QuietFrom = new(2026, 9, 7);
    private static readonly DateOnly Latest = new(2026, 9, 14);

    [Fact]
    public void Two_weeks_under_the_line_fire()
    {
        var finding = CoverageGapDetector.Detect(Weeks(QuietFrom, 0m, 19.99m), First, Latest);

        Assert.NotNull(finding);
        Assert.Equal(2, finding.QuietWeeks);
        Assert.Equal(QuietFrom, finding.QuietFrom);
        Assert.Equal(Usual, finding.BaselineWeeklySpendUsd);
    }

    [Fact]
    public void A_week_at_exactly_twenty_percent_is_not_quiet()
    {
        Assert.Null(CoverageGapDetector.Detect(Weeks(QuietFrom, 0m, 20m), First, Latest));
        Assert.True(CoverageGapDetector.IsQuiet(19.99m, 100m));
        Assert.False(CoverageGapDetector.IsQuiet(20m, 100m));
    }

    [Fact]
    public void One_quiet_week_is_not_enough()
    {
        var weeks = Weeks(QuietFrom.AddDays(7), 0m);

        Assert.Null(CoverageGapDetector.Detect(weeks, First, QuietFrom.AddDays(7)));
    }

    [Fact]
    public void Quiet_week_before_a_normal_one_does_not_fire()
    {
        Assert.Null(CoverageGapDetector.Detect(Weeks(QuietFrom, 0m, Usual), First, Latest));
    }

    [Fact]
    public void The_run_start_is_found_for_a_long_gap()
    {
        var latest = QuietFrom.AddDays(7 * 5);
        var finding = CoverageGapDetector.Detect(Weeks(QuietFrom, 0m, 0m, 0m, 0m, 0m, 0m), First, latest);

        Assert.NotNull(finding);
        Assert.Equal(6, finding.QuietWeeks);
        Assert.Equal(QuietFrom, finding.QuietFrom);
    }

    [Fact]
    public void A_stray_purchase_week_inside_the_gap_does_not_cut_it_short()
    {
        // The week of the first quiet one is followed by a week at 40% of usual, then four more quiet weeks: the gap
        // starts at the first quiet week, not after the stray one.
        var latest = QuietFrom.AddDays(7 * 5);
        var finding = CoverageGapDetector.Detect(Weeks(QuietFrom, 0m, 0m, 40m, 0m, 0m, 0m), First, latest);

        Assert.NotNull(finding);
        Assert.Equal(QuietFrom, finding.QuietFrom);
        Assert.Equal(6, finding.QuietWeeks);
        Assert.Equal(Usual, finding.BaselineWeeklySpendUsd);
    }

    [Fact]
    public void A_busy_week_among_the_latest_two_means_the_gap_is_not_current()
    {
        var latest = QuietFrom.AddDays(7 * 4);
        var finding = CoverageGapDetector.Detect(Weeks(QuietFrom, 0m, 0m, 0m, 40m, 0m), First, latest);

        // The last two weeks are 40 and 0: the gap does not end on two quiet weeks, so there is nothing to alert on yet.
        Assert.Null(finding);
    }

    [Fact]
    public void Two_busy_weeks_in_a_row_end_the_gap_before_them()
    {
        var latest = QuietFrom.AddDays(7 * 5);
        var finding = CoverageGapDetector.Detect(Weeks(QuietFrom, 0m, 0m, 40m, 40m, 0m, 0m), First, latest);

        Assert.NotNull(finding);
        Assert.Equal(QuietFrom.AddDays(7 * 4), finding.QuietFrom);
        Assert.Equal(2, finding.QuietWeeks);
    }

    [Fact]
    public void A_busy_week_is_a_stray_only_when_two_quiet_weeks_follow_it()
    {
        // quiet, busy, quiet, busy, quiet, quiet: the first busy week is followed by one quiet week and then a busy one,
        // so it is not bridged; the gap runs from the quiet week after it.
        var latest = QuietFrom.AddDays(7 * 5);
        var finding = CoverageGapDetector.Detect(Weeks(QuietFrom, 0m, Usual, 0m, Usual, 0m, 0m), First, latest);

        Assert.NotNull(finding);
        Assert.Equal(QuietFrom.AddDays(7 * 2), finding.QuietFrom);
        Assert.Equal(4, finding.QuietWeeks);
    }

    [Fact]
    public void The_gap_starts_on_a_quiet_week_not_on_the_busy_week_before_it()
    {
        var latest = QuietFrom.AddDays(7 * 2);
        var finding = CoverageGapDetector.Detect(Weeks(QuietFrom, 40m, 0m, 0m), First, latest);

        Assert.NotNull(finding);
        Assert.Equal(QuietFrom.AddDays(7), finding.QuietFrom);
        Assert.Equal(2, finding.QuietWeeks);
    }

    [Theory]
    [InlineData(new[] { true, true }, true)]
    [InlineData(new[] { true, false, true, true }, true)]
    [InlineData(new[] { false, true, true }, true)]
    [InlineData(new[] { true, true, false }, false)]
    [InlineData(new[] { true, true, true, false, true }, false)]
    [InlineData(new[] { true, false, false, true, true }, false)]
    [InlineData(new[] { true }, false)]
    public void A_run_is_one_gap_when_it_ends_quiet_and_every_busy_week_is_followed_by_two_quiet_ones(bool[] quiet, bool expected)
        => Assert.Equal(expected, CoverageGapDetector.IsContinuous(quiet));

    [Fact]
    public void An_open_hole_is_moved_back_to_the_first_quiet_week_through_a_stray_week()
    {
        // A hole recorded from the week after the stray one: quiet, quiet, stray, then the recorded start.
        var holeFrom = QuietFrom.AddDays(7 * 3);
        var weeks = Weeks(QuietFrom, 0m, 0m, 40m, 0m, 0m, 0m);

        Assert.Equal(QuietFrom, CoverageGapDetector.FirstQuietWeek(weeks, First, holeFrom, Usual));
    }

    [Fact]
    public void A_hole_that_already_starts_at_its_first_quiet_week_does_not_move()
    {
        var weeks = Weeks(QuietFrom, 0m, 0m, 0m);

        Assert.Equal(QuietFrom, CoverageGapDetector.FirstQuietWeek(weeks, First, QuietFrom, Usual));
    }

    [Fact]
    public void An_open_hole_stops_at_two_busy_weeks_in_a_row()
    {
        var holeFrom = QuietFrom.AddDays(7 * 4);
        var weeks = Weeks(QuietFrom, 0m, 0m, 40m, 40m, 0m, 0m);

        Assert.Equal(holeFrom, CoverageGapDetector.FirstQuietWeek(weeks, First, holeFrom, Usual));
    }

    [Fact]
    public void An_open_hole_never_moves_back_past_the_weeks_of_no_data_before_the_first_spend()
    {
        // Weeks before the account's first spend are absent from the dictionary and would read as zero (quiet).
        var holeFrom = QuietFrom.AddDays(7 * 2);
        var weeks = Weeks(QuietFrom, 0m, 0m, 0m).Where(kv => kv.Key >= QuietFrom.AddDays(-7 * 8)).ToDictionary(kv => kv.Key, kv => kv.Value);

        Assert.Equal(QuietFrom, CoverageGapDetector.FirstQuietWeek(weeks, QuietFrom.AddDays(-7 * 8), holeFrom, Usual));
        Assert.Equal(QuietFrom.AddDays(-7 * 8), CoverageGapDetector.FirstQuietWeek(new Dictionary<DateOnly, decimal>(), QuietFrom.AddDays(-7 * 8), holeFrom, Usual));
    }

    [Fact]
    public void An_open_hole_is_moved_back_at_most_the_longest_run_looked_back_over()
    {
        var holeFrom = QuietFrom.AddDays(7 * 40);

        Assert.Equal(
            holeFrom.AddDays(-7 * CoverageGapDetector.MaxQuietWeeks),
            CoverageGapDetector.FirstQuietWeek(new Dictionary<DateOnly, decimal>(), First, holeFrom, Usual));
    }

    [Fact]
    public void Baseline_needs_the_minimum_weeks_of_history()
    {
        var first = QuietFrom.AddDays(-7 * CoverageGapDetector.MinBaselineWeeks);
        var weeks = Weeks(QuietFrom, 0m, 0m).Where(kv => kv.Key >= first).ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.NotNull(CoverageGapDetector.Detect(weeks, first, Latest));

        var shortFirst = first.AddDays(7);
        var short5 = weeks.Where(kv => kv.Key >= shortFirst).ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.Null(CoverageGapDetector.Detect(short5, shortFirst, Latest));
    }

    [Fact]
    public void Weeks_before_the_first_spend_are_no_data_not_zeros()
    {
        // 8 real weeks then quiet: a median over 13 weeks counting the missing five as zero would be 0 and never fire
        // (or would read the account as always quiet); the first-spend week bounds the window instead.
        var first = QuietFrom.AddDays(-7 * 8);
        var weeks = Weeks(QuietFrom, 0m, 0m).Where(kv => kv.Key >= first).ToDictionary(kv => kv.Key, kv => kv.Value);

        Assert.Equal(Usual, CoverageGapDetector.Detect(weeks, first, Latest)?.BaselineWeeklySpendUsd);
    }

    [Fact]
    public void A_small_baseline_is_not_spend_that_used_to_flow()
    {
        var weeks = new Dictionary<DateOnly, decimal>();
        for (var w = First; w < QuietFrom; w = w.AddDays(7))
            weeks[w] = CoverageGapDetector.MinBaselineWeeklySpendUsd - 0.01m;
        Assert.Null(CoverageGapDetector.Detect(weeks, First, Latest));

        for (var w = First; w < QuietFrom; w = w.AddDays(7))
            weeks[w] = CoverageGapDetector.MinBaselineWeeklySpendUsd;
        Assert.NotNull(CoverageGapDetector.Detect(weeks, First, Latest));
    }

    [Fact]
    public void Baseline_is_the_median_so_one_big_week_does_not_move_it()
    {
        var weeks = Weeks(QuietFrom, 0m, 0m);
        weeks[First.AddDays(14)] = 100_000m;

        Assert.Equal(Usual, CoverageGapDetector.Detect(weeks, First, Latest)?.BaselineWeeklySpendUsd);
    }

    [Theory]
    [InlineData("2026-09-07", "2026-09-07")] // Monday
    [InlineData("2026-09-13", "2026-09-07")] // Sunday
    [InlineData("2026-09-10", "2026-09-07")]
    public void Weeks_start_on_monday(string day, string monday)
        => Assert.Equal(DateOnly.Parse(monday), CoverageGapDetector.WeekStart(DateOnly.Parse(day)));

    [Fact]
    public void The_latest_complete_week_excludes_the_current_one()
        => Assert.Equal(new DateOnly(2026, 9, 14), CoverageGapDetector.LatestCompleteWeek(new DateOnly(2026, 9, 27)));
}
