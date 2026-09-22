namespace FinanceSentry.Modules.Events.Tests;

using FinanceSentry.Modules.Events.Domain;
using FluentAssertions;
using Xunit;

/// <summary>Feature 049 US2: the next periodic filing is derived from EDGAR history, never invented.</summary>
public sealed class FilingDueCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 9, 22);

    private static PeriodicFiling K(int y, int m, int d) => new("10-K", new DateOnly(y, m, d).AddDays(55), new DateOnly(y, m, d));
    private static PeriodicFiling Q(int y, int m, int d) => new("10-Q", new DateOnly(y, m, d).AddDays(35), new DateOnly(y, m, d));

    [Fact]
    public void Quarter_after_a_10Q_is_a_10Q_due_40_days_after_the_period_end()
    {
        var due = FilingDueCalculator.Next([Q(2026, 6, 30), Q(2026, 3, 31), K(2025, 12, 31)], Today);

        due.Should().NotBeNull();
        due!.Form.Should().Be("10-Q");
        due.PeriodEnd.Should().Be(new DateOnly(2026, 9, 30));
        due.DueDate.Should().Be(new DateOnly(2026, 11, 9));
    }

    [Fact]
    public void Quarter_that_closes_the_fiscal_year_is_a_10K_due_60_days_after_the_period_end()
    {
        var due = FilingDueCalculator.Next([Q(2026, 9, 30), Q(2026, 6, 30), K(2025, 12, 31)], new DateOnly(2026, 11, 10));

        due.Should().NotBeNull();
        due!.Form.Should().Be("10-K");
        due.PeriodEnd.Should().Be(new DateOnly(2026, 12, 31));
        due.DueDate.Should().Be(new DateOnly(2027, 3, 1));
    }

    [Fact]
    public void Quarter_after_a_10K_is_the_first_10Q_of_the_new_year()
    {
        var due = FilingDueCalculator.Next([K(2025, 12, 31), Q(2025, 9, 30)], new DateOnly(2026, 3, 1));

        due!.Form.Should().Be("10-Q");
        due.PeriodEnd.Should().Be(new DateOnly(2026, 3, 31));
        due.DueDate.Should().Be(new DateOnly(2026, 5, 10));
    }

    [Fact]
    public void Due_date_already_in_the_past_yields_nothing()
    {
        // Latest period ended 2025-12-31 (10-K): due 2026-03-01, long before today.
        FilingDueCalculator.Next([K(2025, 12, 31)], Today).Should().BeNull();
    }

    [Fact]
    public void No_filings_yields_nothing()
        => FilingDueCalculator.Next([], Today).Should().BeNull();

    [Fact]
    public void Non_periodic_forms_are_ignored()
        => FilingDueCalculator.Next([new PeriodicFiling("8-K", Today, Today)], Today).Should().BeNull();

    [Fact]
    public void Without_a_10K_on_record_the_next_filing_is_assumed_quarterly()
    {
        var due = FilingDueCalculator.Next([Q(2026, 6, 30)], Today);

        due!.Form.Should().Be("10-Q");
        due.DueDate.Should().Be(new DateOnly(2026, 11, 9));
    }

    [Fact]
    public void Month_end_arithmetic_clamps_to_the_shorter_month()
    {
        // Nov 30 + 3 months = Feb 28 (2027 is not a leap year); 10-Q due 40 days later.
        var due = FilingDueCalculator.Next([Q(2026, 11, 30), K(2026, 5, 31)], new DateOnly(2027, 1, 5));

        due!.PeriodEnd.Should().Be(new DateOnly(2027, 2, 28));
        due.DueDate.Should().Be(new DateOnly(2027, 4, 9));
    }

    [Fact]
    public void Period_end_snaps_to_the_month_end_for_a_52_week_filer()
    {
        // A fiscal quarter that ended 27 June still owes its next 10-Q for the quarter ending 30 September.
        var due = FilingDueCalculator.Next([Q(2026, 6, 27), K(2025, 12, 27)], Today);

        due!.PeriodEnd.Should().Be(new DateOnly(2026, 9, 30));
        due.DueDate.Should().Be(new DateOnly(2026, 11, 9));
    }

    [Fact]
    public void Form_matching_is_case_insensitive_and_picks_the_latest_period()
    {
        var filings = new List<PeriodicFiling>
        {
            new("10-q", new DateOnly(2026, 5, 5), new DateOnly(2026, 3, 31)),
            new("10-Q", new DateOnly(2026, 8, 5), new DateOnly(2026, 6, 30)),
            new("10-k", new DateOnly(2026, 2, 20), new DateOnly(2025, 12, 31)),
        };

        FilingDueCalculator.Next(filings, Today)!.PeriodEnd.Should().Be(new DateOnly(2026, 9, 30));
    }
}
