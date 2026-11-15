namespace FinanceSentry.Modules.Research.Domain.Valuation;

/// <summary>
/// Sector rules shared by the valuation tools (#1269). Banks and insurers earn from their balance sheet, so
/// free cash flow and EV/EBITDA are not meaningful for them: the DCF refuses them and comps compares them on
/// P/E and P/B only.
/// </summary>
public static class ValuationSectors
{
    public static bool IsFinancial(string? industry)
        => industry is not null
            && (industry.StartsWith("Banks", StringComparison.OrdinalIgnoreCase)
                || industry.StartsWith("Insurance -", StringComparison.OrdinalIgnoreCase));
}
