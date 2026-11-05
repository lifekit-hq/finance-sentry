namespace FinanceSentry.Modules.Research.Domain.Tax;

/// <summary>
/// One tax year's Irish rates for individuals. <see cref="Year"/> is the calendar year the rates were set for;
/// <see cref="Source"/> names where each value was read.
/// </summary>
public sealed record IrishTaxRates(
    int Year,
    decimal CgtRate,
    decimal CgtAnnualExemptionEur,
    decimal ExitTaxRate,
    int DeemedDisposalYears,
    string Source);

/// <summary>
/// Versioned Irish disposal-tax reference, keyed by tax year. RE-CHECK EACH TAX YEAR: add the new year's row
/// when Finance Act changes land, and never edit a past row. A year with no row of its own reads the latest
/// earlier row and says so (<see cref="Rates.CarriedForward"/>), so the estimate never silently assumes a rate
/// nobody has checked. A rate announced for a later year is not entered until it is enacted.
/// </summary>
public static class IrishTaxReference
{
    /// <summary>The rates in force for a year and whether they come from an earlier year's row.</summary>
    public sealed record Rates(IrishTaxRates Row, bool CarriedForward);

    // 2026:
    //  - CGT 33% and the EUR 1,270 annual exemption (per individual, per year, shared by all disposals):
    //    https://www.revenue.ie/en/gains-gifts-and-inheritance/transfering-an-asset/selling-or-disposing-of-shares.aspx
    //  - Exit tax 38% on investment-undertaking gains for individuals (Finance Act 2025, chargeable events from
    //    1 Jan 2026) and the 8-year deemed disposal: Revenue Tax and Duty Manual Part 27-01a-02,
    //    https://www.revenue.ie/en/tax-professionals/tdm/income-tax-capital-gains-tax-corporation-tax/part-27/27-01a-02.pdf
    //    A cut to 35% from 1 Jan 2027 was announced in Budget 2027 but was not enacted when this row was written.
    private static readonly IReadOnlyList<IrishTaxRates> Table =
    [
        new(
            Year: 2026,
            CgtRate: 0.33m,
            CgtAnnualExemptionEur: 1270m,
            ExitTaxRate: 0.38m,
            DeemedDisposalYears: 8,
            Source: "revenue.ie: selling-or-disposing-of-shares (CGT); Tax and Duty Manual Part 27-01a-02 (exit tax)"),
    ];

    public static Rates ForYear(int year)
    {
        var exact = Table.FirstOrDefault(r => r.Year == year);
        if (exact is not null)
        {
            return new Rates(exact, false);
        }

        // The latest row not after the year; for a year before the table starts, the earliest row.
        var earlier = Table.Where(r => r.Year < year).OrderByDescending(r => r.Year).FirstOrDefault()
            ?? Table.OrderBy(r => r.Year).First();
        return new Rates(earlier, true);
    }
}
