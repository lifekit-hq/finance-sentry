namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.Domain;

/// <summary>
/// The authoritative macro release schedule the calendar is reconciled against (#1091), read from each publisher's
/// own calendar: BLS release schedules for CPI and the Employment Situation (08:30 ET), the Fed's FOMC meeting
/// calendar (statement at 14:00 ET on the second day of the meeting) and the ECB's monetary policy calendar (decision
/// at 14:15 Frankfurt time). Dates and times are the publisher's wall clock; <see cref="MacroEventTiming"/> resolves
/// the instant. The first hand-built seed (#250) dated the September CPI on 15 October when BLS released it on the
/// 14th, every FOMC decision a day late, five payrolls releases and the ECB time wrong — fix a date here and the
/// daily sync corrects the stored row.
/// </summary>
public static class MacroCalendarSchedule
{
    private const string Fomc = "https://www.federalreserve.gov/monetarypolicy/fomccalendars.htm";
    private const string Cpi = "https://www.bls.gov/schedule/news_release/cpi.htm";
    private const string Payrolls = "https://www.bls.gov/schedule/news_release/empsit.htm";
    private const string Ecb = "https://www.ecb.europa.eu/press/calendars/mgcgc/html/index.en.html";

    public static IReadOnlyList<MacroEvent> Events() =>
    [
        Ev(2026, 7, 29, "FOMC rate decision", "US", Fomc, 14, 0),
        Ev(2026, 9, 16, "FOMC rate decision + SEP", "US", Fomc, 14, 0),
        Ev(2026, 10, 28, "FOMC rate decision", "US", Fomc, 14, 0),
        Ev(2026, 12, 9, "FOMC rate decision + SEP", "US", Fomc, 14, 0),

        Ev(2026, 7, 14, "CPI (Jun)", "US", Cpi, 8, 30),
        Ev(2026, 8, 12, "CPI (Jul)", "US", Cpi, 8, 30),
        Ev(2026, 9, 11, "CPI (Aug)", "US", Cpi, 8, 30),
        Ev(2026, 10, 14, "CPI (Sep)", "US", Cpi, 8, 30),
        Ev(2026, 11, 10, "CPI (Oct)", "US", Cpi, 8, 30),
        Ev(2026, 12, 10, "CPI (Nov)", "US", Cpi, 8, 30),

        Ev(2026, 7, 2, "Nonfarm payrolls (Jun)", "US", Payrolls, 8, 30),
        Ev(2026, 8, 7, "Nonfarm payrolls (Jul)", "US", Payrolls, 8, 30),
        Ev(2026, 9, 4, "Nonfarm payrolls (Aug)", "US", Payrolls, 8, 30),
        Ev(2026, 10, 2, "Nonfarm payrolls (Sep)", "US", Payrolls, 8, 30),
        Ev(2026, 11, 6, "Nonfarm payrolls (Oct)", "US", Payrolls, 8, 30),
        Ev(2026, 12, 4, "Nonfarm payrolls (Nov)", "US", Payrolls, 8, 30),

        Ev(2026, 7, 23, "ECB rate decision", "EU", Ecb, 14, 15),
        Ev(2026, 9, 10, "ECB rate decision + projections", "EU", Ecb, 14, 15),
        Ev(2026, 10, 29, "ECB rate decision", "EU", Ecb, 14, 15),
        Ev(2026, 12, 17, "ECB rate decision + projections", "EU", Ecb, 14, 15),
    ];

    private static MacroEvent Ev(int y, int m, int d, string ev, string region, string src, int hour, int minute)
        => new()
        {
            EventDate = new DateOnly(y, m, d),
            EventTime = new TimeOnly(hour, minute),
            Event = ev,
            Region = region,
            Importance = MacroImportance.High,
            Source = src,
        };
}
