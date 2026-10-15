namespace FinanceSentry.Modules.Research.Tests.MacroCalendar;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FluentAssertions;
using Xunit;

/// <summary>
/// #1091 reproduction: the first seed dated the September 2026 CPI on 15 October, BLS released it on the 14th, and a
/// populated table was never re-seeded, so on the evening of the 14th the row still read as tomorrow's event. The
/// sync must flag that row, move it to the publisher's date, and leave rows the schedule does not know alone.
/// </summary>
public sealed class MacroCalendarReconcilerTests
{
    private const string Cpi = "https://www.bls.gov/schedule/news_release/cpi.htm";

    // The evening the agent announced "tomorrow: CPI" — the print was already out that morning.
    private static readonly DateTimeOffset EveningOfTheRelease = new(2026, 10, 14, 20, 0, 0, TimeSpan.Zero);

    private static MacroEvent Row(
        int month, int day, string name, int hour = 8, int minute = 30, string region = "US", string source = Cpi,
        DateTimeOffset? ingestedAt = null) => new()
    {
        EventDate = new DateOnly(2026, month, day),
        EventTime = new TimeOnly(hour, minute),
        Event = name,
        Region = region,
        Importance = MacroImportance.High,
        Source = source,
        IngestedAt = ingestedAt ?? new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private static IReadOnlyList<MacroEvent> Schedule => [Row(10, 14, "CPI (Sep)")];

    [Fact]
    public void Misdated_row_that_still_reads_as_upcoming_after_the_release_is_flagged_and_corrected()
    {
        var stale = Row(10, 15, "CPI (Sep)");

        var plan = MacroCalendarReconciler.Plan([stale], Schedule, EveningOfTheRelease);

        var flag = plan.Flags.Should().ContainSingle().Subject;
        flag.Key.Should().Be("macro:CPI:2026-10");
        flag.EventId.Should().Be(stale.Id);
        flag.StoredAt.Should().Be(new DateTimeOffset(2026, 10, 15, 12, 30, 0, TimeSpan.Zero));
        flag.ReleasedAt.Should().Be(new DateTimeOffset(2026, 10, 14, 12, 30, 0, TimeSpan.Zero));

        var correction = plan.Corrections.Should().ContainSingle().Subject;
        correction.Apply();
        stale.EventDate.Should().Be(new DateOnly(2026, 10, 14));
        stale.EventTime.Should().Be(new TimeOnly(8, 30));
        MacroEventTiming.StatusAt(MacroEventTiming.EventAt(stale), EveningOfTheRelease)
            .Should().Be(MacroEventStatus.Released);

        plan.Inserts.Should().BeEmpty();
        plan.Deletes.Should().BeEmpty();
    }

    [Fact]
    public void Misdated_row_corrected_before_the_release_is_corrected_but_not_flagged()
    {
        var stale = Row(10, 15, "CPI (Sep)");

        var plan = MacroCalendarReconciler.Plan(
            [stale], Schedule, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        plan.Flags.Should().BeEmpty();
        plan.Corrections.Should().ContainSingle();
    }

    [Fact]
    public void Row_matching_the_schedule_is_left_unchanged()
    {
        var plan = MacroCalendarReconciler.Plan([Row(10, 14, "CPI (Sep)")], Schedule, EveningOfTheRelease);

        plan.Flags.Should().BeEmpty();
        plan.Corrections.Should().BeEmpty();
        plan.Inserts.Should().BeEmpty();
        plan.Deletes.Should().BeEmpty();
    }

    [Fact]
    public void Wrong_time_on_the_right_date_is_corrected()
    {
        const string ecb = "https://www.ecb.europa.eu/press/calendars/mgcgc/html/index.en.html";
        var stored = Row(10, 29, "ECB rate decision", 13, 15, "EU", ecb);

        var plan = MacroCalendarReconciler.Plan(
            [stored], [Row(10, 29, "ECB rate decision", 14, 15, "EU", ecb)], EveningOfTheRelease);

        plan.Corrections.Should().ContainSingle().Which.Stored.Should().BeSameAs(stored);
    }

    [Fact]
    public void Release_missing_from_the_table_is_inserted()
    {
        var plan = MacroCalendarReconciler.Plan([], Schedule, EveningOfTheRelease);

        plan.Inserts.Should().ContainSingle().Which.Event.Should().Be("CPI (Sep)");
    }

    [Fact]
    public void Release_stored_twice_keeps_the_row_on_the_scheduled_date_and_drops_the_other()
    {
        var stale = Row(10, 15, "CPI (Sep)", ingestedAt: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var right = Row(10, 14, "CPI (Sep)", ingestedAt: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

        var plan = MacroCalendarReconciler.Plan([stale, right], Schedule, EveningOfTheRelease);

        plan.Deletes.Should().ContainSingle().Which.Should().BeSameAs(stale);
        plan.Corrections.Should().BeEmpty();
        plan.Flags.Should().BeEmpty();
    }

    [Fact]
    public void Row_the_schedule_does_not_know_is_left_alone()
    {
        var pmi = Row(10, 22, "PMI (Oct)", source: "manual");

        var plan = MacroCalendarReconciler.Plan([pmi], Schedule, EveningOfTheRelease);

        plan.Deletes.Should().BeEmpty();
        plan.Corrections.Should().BeEmpty();
        plan.Inserts.Should().ContainSingle();
    }

    [Fact]
    public void Schedule_releases_have_unique_keys()
    {
        // The key is the reconcile join and the fire-once reference, so two releases sharing one would be merged.
        MacroCalendarSchedule.Events()
            .Select(e => (MacroEventTiming.Key(e), e.Region))
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Schedule_dates_the_september_2026_cpi_on_the_14th()
    {
        MacroCalendarSchedule.Events().Should().ContainSingle(e => e.Event == "CPI (Sep)")
            .Which.EventDate.Should().Be(new DateOnly(2026, 10, 14));
    }
}
