namespace FinanceSentry.Core.Utils;

/// <summary>
/// Converts foreign amounts to USD using a process-wide rate table.
///
/// The table seeds with a small hardcoded fallback so conversion always works
/// offline (or before the first refresh). A background job replaces it with live
/// rates via <see cref="UpdateRates"/>. Rates are "USD per 1 unit" multipliers
/// (e.g. UAH ≈ 0.024), so <see cref="ToUsd"/> is a plain multiply.
///
/// Unknown currencies fall back to 1:1 — callers should treat totals mixing
/// unlisted currencies as approximate.
/// </summary>
public static class CurrencyConverter
{
    /// <summary>Offline seed. Also the source of truth when the refresh job has never run.</summary>
    public static readonly IReadOnlyDictionary<string, decimal> FallbackRates =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = 1.00m,
            ["EUR"] = 1.08m,
            ["GBP"] = 1.27m,
            ["UAH"] = 0.024m,
        };

    /// <summary>Table plus the moment it was installed, swapped as one so a reader that checks
    /// freshness before reading rates can never pair a live stamp with the seed table.</summary>
    private sealed record RateTable(IReadOnlyDictionary<string, decimal> Rates, DateTimeOffset? UpdatedAtUtc);

    // Immutable snapshot swapped by reference so reads never see a partial table.
    private static volatile RateTable _table = new(FallbackRates, null);

    /// <summary>
    /// When the live table was last installed by <see cref="UpdateRates"/>, or null while the
    /// hardcoded seed is still in force. Callers that compare a rate to another rate — rather
    /// than merely normalising magnitudes — must consult <see cref="AreRatesFresh"/> first.
    /// </summary>
    public static DateTimeOffset? RatesUpdatedAtUtc => _table.UpdatedAtUtc;

    /// <summary>
    /// True when a live refresh landed less than <paramref name="maxAge"/> ago. False while the
    /// seed is still in force, and false once the feed has been down long enough for the table to
    /// drift. A non-positive <paramref name="maxAge"/> is never fresh, so it switches a
    /// freshness-gated caller off outright.
    /// <para>
    /// Normalising a mixed-currency total tolerates a stale rate — the total is approximate either
    /// way. Judging one rate <em>against</em> another does not: the whole measurement is the gap
    /// between them, so a stale reference manufactures a gap that no bank charged.
    /// </para>
    /// </summary>
    public static bool AreRatesFresh(TimeSpan maxAge) =>
        _table.UpdatedAtUtc is { } updatedAt && DateTimeOffset.UtcNow - updatedAt < maxAge;

    public static decimal ToUsd(decimal amount, string currency)
    {
        if (!string.IsNullOrWhiteSpace(currency) && _table.Rates.TryGetValue(currency, out var rate))
            return amount * rate;

        return amount;
    }

    /// <summary>
    /// True when a rate is available for <paramref name="currency"/>. Callers building a total
    /// should check this to flag the result as approximate rather than trusting a silent 1:1
    /// fallback for an unlisted currency.
    /// </summary>
    public static bool IsKnown(string? currency) =>
        !string.IsNullOrWhiteSpace(currency) && _table.Rates.ContainsKey(currency);

    /// <summary>
    /// Replaces the live rate table (USD-per-unit multipliers). The hardcoded
    /// fallback is merged underneath so a currency briefly missing from the feed
    /// still resolves. Ignored when <paramref name="rates"/> is null/empty — a feed
    /// outage must not pass for a refresh, so the freshness stamp stays put too.
    /// </summary>
    public static void UpdateRates(IReadOnlyDictionary<string, decimal>? rates)
    {
        if (rates is null || rates.Count == 0)
            return;

        var merged = new Dictionary<string, decimal>(FallbackRates, StringComparer.OrdinalIgnoreCase);
        foreach (var (currency, rate) in rates)
        {
            if (rate > 0m)
                merged[currency] = rate;
        }

        merged["USD"] = 1.00m;
        _table = new RateTable(merged, DateTimeOffset.UtcNow);
    }
}
