namespace FinanceSentry.Modules.Research.Tests.MacroCalendar;

using FinanceSentry.Modules.Research.Domain;
using FluentAssertions;
using Xunit;

/// <summary>
/// #1091: a macro row's date and time are the publisher's wall clock, so the release instant, the status derived
/// from it and the release key must come out the same whatever zone the server or the reader is in.
/// </summary>
public sealed class MacroEventTimingTests
{
    [Fact]
    public void Us_release_resolves_in_eastern_time()
    {
        // BLS released the September 2026 CPI at 08:30 EDT on 14 October: 12:30Z.
        MacroEventTiming.EventAt(new DateOnly(2026, 10, 14), new TimeOnly(8, 30), "US")
            .Should().Be(new DateTimeOffset(2026, 10, 14, 12, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Us_release_after_the_clocks_change_uses_standard_time()
    {
        MacroEventTiming.EventAt(new DateOnly(2026, 12, 10), new TimeOnly(8, 30), "US")
            .Should().Be(new DateTimeOffset(2026, 12, 10, 13, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Eu_release_resolves_in_frankfurt_time()
    {
        MacroEventTiming.EventAt(new DateOnly(2026, 10, 29), new TimeOnly(14, 15), "EU")
            .Should().Be(new DateTimeOffset(2026, 10, 29, 13, 15, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Untimed_row_counts_from_the_start_of_its_local_day()
    {
        MacroEventTiming.EventAt(new DateOnly(2026, 10, 14), null, "US")
            .Should().Be(new DateTimeOffset(2026, 10, 14, 4, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Unknown_region_resolves_in_utc()
    {
        MacroEventTiming.EventAt(new DateOnly(2026, 10, 14), new TimeOnly(9, 0), "JP")
            .Should().Be(new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Status_is_released_from_the_release_instant_on()
    {
        var at = new DateTimeOffset(2026, 10, 14, 12, 30, 0, TimeSpan.Zero);

        MacroEventTiming.StatusAt(at, at.AddTicks(-1)).Should().Be(MacroEventStatus.Scheduled);
        MacroEventTiming.StatusAt(at, at).Should().Be(MacroEventStatus.Released);
        MacroEventTiming.StatusAt(at, at.AddDays(1)).Should().Be(MacroEventStatus.Released);
    }

    [Theory]
    [InlineData("CPI (Sep)", "CPI")]
    [InlineData("Nonfarm payrolls (Sep)", "NFP")]
    [InlineData("FOMC rate decision", "FOMC")]
    [InlineData("FOMC rate decision + SEP", "FOMC")]
    [InlineData("ECB rate decision + projections", "ECB")]
    [InlineData("Retail sales (Sep)", "RETAIL-SALES")]
    public void Family_names_the_release_not_the_period(string eventName, string family)
        => MacroEventTiming.Family(eventName).Should().Be(family);

    [Fact]
    public void Key_names_the_release_month_so_a_date_corrected_within_it_keeps_its_key()
    {
        MacroEventTiming.Key("CPI (Sep)", new DateOnly(2026, 10, 15)).Should().Be("macro:CPI:2026-10");
        MacroEventTiming.Key("CPI (Sep)", new DateOnly(2026, 10, 14)).Should().Be("macro:CPI:2026-10");
    }

    [Theory]
    [InlineData("scheduled", true)]
    [InlineData("released", true)]
    [InlineData("Scheduled", false)]
    [InlineData("tomorrow", false)]
    [InlineData(null, false)]
    public void Only_the_two_statuses_are_known(string? status, bool known)
        => MacroEventStatus.IsKnown(status).Should().Be(known);
}
