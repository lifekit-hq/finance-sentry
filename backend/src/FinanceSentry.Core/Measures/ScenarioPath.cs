namespace FinanceSentry.Core.Measures;

/// <summary>What a dated career-money event does to the monthly saving (#454 S5).</summary>
public enum ScenarioEventKind
{
    /// <summary>Income changes from the date on by a signed monthly amount (a raise is positive, a cut negative).</summary>
    IncomeStep,

    /// <summary>A single amount received in the month of the date.</summary>
    OneOffIn,

    /// <summary>A single amount spent in the month of the date.</summary>
    OneOffOut,

    /// <summary>Income falls by a positive monthly amount from the date through the end date, inclusive.</summary>
    Leave,
}

/// <summary>
/// One counted event, already in USD: <paramref name="AmountUsd"/> is a signed monthly change for an
/// <see cref="ScenarioEventKind.IncomeStep"/> and a positive amount for the other kinds.
/// <paramref name="EndDate"/> is set for a <see cref="ScenarioEventKind.Leave"/> only.
/// </summary>
public sealed record ScenarioEvent(ScenarioEventKind Kind, DateOnly Date, DateOnly? EndDate, decimal AmountUsd);

/// <summary>
/// Turns the person's entered events into the month-by-month changes to the baseline monthly saving that
/// <see cref="Projection"/> takes. Pure: no I/O, no clock.
/// <para>
/// An event dated on or before <c>today</c> is not applied: the baseline savings path is the median of the trailing complete
/// months, so a raise that already happened is in it, and counting it again would double it. The one exception is a
/// <see cref="ScenarioEventKind.Leave"/> that has started but not ended: the months of it still ahead are in no complete month,
/// so they are applied from the first month on. Every change is a change to the <b>saving</b>: spending is assumed not to move with it.
/// </para>
/// </summary>
public static class ScenarioPath
{
    private const int MonthsPerYear = 12;

    /// <summary>
    /// The change to the monthly saving for each of the next <paramref name="months"/> months (index i is the (i + 1)th month
    /// from <paramref name="today"/>, the one whose end the projection compounds; an event lands at the index of the whole
    /// months from today to its date, so one 10 days out is index 0 and one 40 days out index 1; a leave under way starts at index 0). An event past the horizon, a leave that ends before it starts, or a leave already over adds nothing.
    /// </summary>
    public static IReadOnlyList<decimal> MonthlyDeltas(IEnumerable<ScenarioEvent> events, DateOnly today, int months)
    {
        var deltas = new decimal[Math.Max(0, months)];
        foreach (var e in events)
        {
            if (e.Kind == ScenarioEventKind.Leave)
            {
                if (e.EndDate is { } end && end >= e.Date && end > today)
                {
                    var from = e.Date > today ? e.Date : today;
                    var start = WholeMonthsUntil(today, from);
                    Add(deltas, start, start + LeaveMonths(from, end) - 1, -e.AmountUsd);
                }

                continue;
            }

            if (e.Date <= today)
                continue;

            var first = WholeMonthsUntil(today, e.Date);
            switch (e.Kind)
            {
                case ScenarioEventKind.IncomeStep:
                    Add(deltas, first, deltas.Length - 1, e.AmountUsd);
                    break;
                case ScenarioEventKind.OneOffIn:
                    Add(deltas, first, first, e.AmountUsd);
                    break;
                case ScenarioEventKind.OneOffOut:
                    Add(deltas, first, first, -e.AmountUsd);
                    break;
            }
        }

        return deltas;
    }

    /// <summary>
    /// How many monthly savings a leave costs: its length in whole months, counting the end date itself, and at least one
    /// (a leave of three calendar months costs three, however the projection's months line up with the calendar).
    /// </summary>
    public static int LeaveMonths(DateOnly start, DateOnly end) => Math.Max(1, WholeMonthsUntil(start, end.AddDays(1)));

    /// <summary>Whole calendar months from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static int WholeMonthsUntil(DateOnly from, DateOnly to)
    {
        var months = (to.Year - from.Year) * MonthsPerYear + to.Month - from.Month;
        return to.Day < from.Day ? months - 1 : months;
    }

    private static void Add(decimal[] deltas, int first, int last, decimal amount)
    {
        for (var i = Math.Max(0, first); i <= last && i < deltas.Length; i++)
            deltas[i] += amount;
    }
}
