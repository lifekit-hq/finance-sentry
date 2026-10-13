namespace FinanceSentry.Modules.Research.Domain;

/// <summary>
/// Rounding for per-unit prices (last close, rebased benchmark, average cost). Money amounts round to cents;
/// a price keeps at least four decimals and gains one per leading zero below 1, so a sub-cent crypto quote
/// (0.000012) keeps its significant digits instead of rounding to zero.
/// </summary>
public static class PriceRounding
{
    private const int MinDecimals = 4;
    private const int MaxDecimals = 12;
    private const decimal Ten = 10m;

    public static decimal Round(decimal price)
    {
        var magnitude = Math.Abs(price);
        var decimals = MinDecimals;
        for (var threshold = 1m; magnitude > 0m && magnitude < threshold && decimals < MaxDecimals; threshold /= Ten)
        {
            decimals++;
        }

        return Math.Round(price, decimals);
    }
}
