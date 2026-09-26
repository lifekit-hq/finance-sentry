namespace FinanceSentry.Modules.BankSync.Application.Services;

using System.Globalization;

/// <summary>
/// Validates a currency code against the real ISO 4217 list, built once from every region .NET
/// knows about rather than a hand-maintained subset — the expected-inflow currency (e.g. a rent
/// figure billed in a currency the app has no exchange rate for yet) must still be accepted as a
/// valid code even though <see cref="Core.Utils.CurrencyConverter"/> only converts a handful.
/// </summary>
public static class Iso4217Currency
{
    private static readonly Lazy<HashSet<string>> Codes = new(BuildCodes);

    /// <summary>True when <paramref name="currency"/> is a known ISO 4217 alphabetic code.</summary>
    public static bool IsValid(string? currency) =>
        !string.IsNullOrWhiteSpace(currency) && Codes.Value.Contains(currency.Trim().ToUpperInvariant());

    private static HashSet<string> BuildCodes()
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var symbol = new RegionInfo(culture.Name).ISOCurrencySymbol;
                if (!string.IsNullOrWhiteSpace(symbol))
                    codes.Add(symbol);
            }
            catch (ArgumentException)
            {
                // Some specific cultures have no matching region (e.g. custom/invariant-like
                // entries) — RegionInfo throws for those; skip rather than fail the whole build.
            }
        }

        return codes;
    }
}
