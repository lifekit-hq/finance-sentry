namespace FinanceSentry.Modules.Research.Domain;

/// <summary>
/// Yahoo quotes some exchanges in a minor unit (LSE pence "GBp"/"GBX", Johannesburg cents "ZAc", Tel Aviv
/// agorot "ILA"). <see cref="Normalize"/> maps the quoted label to the major currency and returns the divisor
/// that converts a quoted price into it; every reader of a quoted price applies it once, so a figure and its
/// currency label never disagree.
/// </summary>
public static class MinorUnitCurrency
{
    private const decimal MinorUnitsPerMajor = 100m;

    public static (string? Currency, decimal Divisor) Normalize(string? quoted)
        => quoted?.Trim() switch
        {
            null or "" => (null, 1m),
            "GBp" or "GBX" => ("GBP", MinorUnitsPerMajor),
            "ZAc" => ("ZAR", MinorUnitsPerMajor),
            "ILA" => ("ILS", MinorUnitsPerMajor),
            var other => (other.ToUpperInvariant(), 1m),
        };
}
