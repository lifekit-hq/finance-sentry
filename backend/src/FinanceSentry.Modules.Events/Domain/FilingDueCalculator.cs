namespace FinanceSentry.Modules.Events.Domain;

/// <summary>A periodic SEC filing (10-K or 10-Q) with the period it covers.</summary>
public sealed record PeriodicFiling(string Form, DateOnly FilingDate, DateOnly ReportDate);

/// <summary>The next periodic filing a ticker owes: its form, the period it will cover, when it is due.</summary>
public sealed record FilingDue(string Form, DateOnly PeriodEnd, DateOnly DueDate);

/// <summary>
/// Derives the next 10-Q / 10-K due date from a ticker's recent periodic filings (feature 049, US2).
/// No feed publishes SEC due dates, so the calculator reasons from what EDGAR already shows: the
/// next period ends three months after the latest periodic filing's period end; it is a 10-K when
/// that month is the fiscal-year-end month (the last 10-K's period-end month), otherwise a 10-Q; the
/// statutory deadline is the large-accelerated-filer one (60 / 40 days) because the filer category
/// is not parsed, which is why every result is an estimate. A due date already in the past yields
/// nothing - the landed detector owns what actually filed, and this never fabricates a date.
/// Pure: no clock, no I/O.
/// </summary>
public static class FilingDueCalculator
{
    public const string AnnualForm = "10-K";
    public const string QuarterlyForm = "10-Q";
    public const int AnnualDeadlineDays = 60;
    public const int QuarterlyDeadlineDays = 40;
    private const int MonthsPerQuarter = 3;

    public static FilingDue? Next(IReadOnlyList<PeriodicFiling> filings, DateOnly today)
    {
        if (filings.Count == 0)
        {
            return null;
        }

        PeriodicFiling? latest = null;
        PeriodicFiling? latestAnnual = null;
        foreach (var f in filings)
        {
            var isAnnual = string.Equals(f.Form, AnnualForm, StringComparison.OrdinalIgnoreCase);
            var isQuarterly = string.Equals(f.Form, QuarterlyForm, StringComparison.OrdinalIgnoreCase);
            if (!isAnnual && !isQuarterly)
            {
                continue;
            }

            if (latest is null || f.ReportDate > latest.ReportDate)
            {
                latest = f;
            }

            if (isAnnual && (latestAnnual is null || f.ReportDate > latestAnnual.ReportDate))
            {
                latestAnnual = f;
            }
        }

        if (latest is null)
        {
            return null;
        }

        var periodEnd = EndOfMonth(latest.ReportDate.AddMonths(MonthsPerQuarter));
        var annual = latestAnnual is not null && periodEnd.Month == latestAnnual.ReportDate.Month;
        var form = annual ? AnnualForm : QuarterlyForm;
        var dueDate = periodEnd.AddDays(annual ? AnnualDeadlineDays : QuarterlyDeadlineDays);

        return dueDate < today ? null : new FilingDue(form, periodEnd, dueDate);
    }

    // Fiscal periods end on a month end for the vast majority of filers; a 52/53-week year ends a
    // few days off it, and the snap is what keeps a 30 September quarter from producing a 30
    // December one. Every result is an estimate either way.
    private static DateOnly EndOfMonth(DateOnly date)
        => new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
}
