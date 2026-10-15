namespace FinanceSentry.Core.Utils;

/// <summary>
/// Converts foreign amounts to USD using a process-wide rate table.
///
/// The table seeds with a small hardcoded fallback so conversion always works
/// offline (or before the first refresh). A background job replaces it with live
/// rates via <see cref="UpdateRates"/>. Rates are "USD per 1 unit" multipliers
/// (e.g. UAH ≈ 0.024), so <see cref="ToUsd"/> is a plain multiply.
///
/// Unknown currencies fall back to 1:1. <see cref="Convert"/> returns that substitution as
/// <see cref="FxStatus.Unknown1To1"/>, and a seed-table rate as <see cref="FxStatus.SeedTable"/>,
/// so a headline figure can carry it; <see cref="ToUsd"/> is the amount-only adapter for callers
/// outside the headline paths.
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
    /// <param name="LiveCurrencies">Currencies whose rate came from the last live refresh; every other
    /// currency in <paramref name="Rates"/> is still the seed's.</param>
    private sealed record RateTable(
        IReadOnlyDictionary<string, decimal> Rates,
        DateTimeOffset? UpdatedAtUtc,
        IReadOnlySet<string> LiveCurrencies);

    private static readonly IReadOnlySet<string> NoLiveCurrencies =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // Immutable snapshot swapped by reference so reads never see a partial table.
    private static volatile RateTable _table = new(FallbackRates, null, NoLiveCurrencies);

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

    /// <summary>
    /// Converts to USD at the rate table in force and says how the rate was obtained: live
    /// (<see cref="FxStatus.Exact"/>), the offline seed (<see cref="FxStatus.SeedTable"/>), or none, with
    /// the amount passed through 1:1 (<see cref="FxStatus.Unknown1To1"/>). Never throws.
    /// </summary>
    public static ConvertResult Convert(decimal amount, string? currency)
    {
        var table = _table;
        if (string.IsNullOrWhiteSpace(currency) || !table.Rates.TryGetValue(currency, out var rate))
            return new ConvertResult(amount, FxStatus.Unknown1To1);

        // USD is its own rate: nothing is substituted, whatever the table's age.
        var live = string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase)
            || table.LiveCurrencies.Contains(currency);
        return new ConvertResult(amount * rate, live ? FxStatus.Exact : FxStatus.SeedTable);
    }

    /// <summary>Amount-only adapter over <see cref="Convert"/>; the status is dropped.</summary>
    public static decimal ToUsd(decimal amount, string currency) => Convert(amount, currency).Amount;

    /// <summary>
    /// The inverse of <see cref="ToUsd"/>: expresses a USD amount in <paramref name="currency"/>.
    /// A currency without a rate (or a missing one) leaves the amount in USD, matching
    /// <see cref="ToUsd"/>'s 1:1 fallback; pair it with <see cref="ResolveBase"/> so the label
    /// follows the figure.
    /// </summary>
    public static decimal FromUsd(decimal usd, string? currency) => ConvertFromUsd(usd, currency).Amount;

    /// <summary>
    /// <see cref="FromUsd"/> with its status, as <see cref="Convert"/> reports it: a currency with no
    /// rate leaves the amount in USD and says so with <see cref="FxStatus.Unknown1To1"/>.
    /// </summary>
    public static ConvertResult ConvertFromUsd(decimal usd, string? currency)
    {
        var table = _table;
        if (string.IsNullOrWhiteSpace(currency) || !table.Rates.TryGetValue(currency, out var rate) || rate <= 0m)
            return new ConvertResult(usd, FxStatus.Unknown1To1);

        var live = string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase)
            || table.LiveCurrencies.Contains(currency);
        return new ConvertResult(usd / rate, live ? FxStatus.Exact : FxStatus.SeedTable);
    }

    /// <summary>
    /// The currency a base-currency total is actually expressed in: the profile's choice when a
    /// rate exists for it, otherwise "USD" (an unset profile, or one with no rate such as BTC).
    /// </summary>
    public static string ResolveBase(string? baseCurrency) =>
        IsKnown(baseCurrency) ? baseCurrency!.ToUpperInvariant() : "USD";

    /// <summary>
    /// True when a rate is available for <paramref name="currency"/>. Callers building a total
    /// should check this to flag the result as approximate rather than trusting a silent 1:1
    /// fallback for an unlisted currency.
    /// </summary>
    public static bool IsKnown(string? currency) =>
        !string.IsNullOrWhiteSpace(currency) && _table.Rates.ContainsKey(currency);

    /// <summary>
    /// Puts the hardcoded seed back in force with no freshness stamp - the state of a process that
    /// has not yet run its first refresh. Lets a test start from a known table.
    /// </summary>
    public static void ResetToSeed() => _table = new RateTable(FallbackRates, null, NoLiveCurrencies);

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
        var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (currency, rate) in rates)
        {
            if (rate <= 0m)
                continue;

            merged[currency] = rate;
            live.Add(currency);
        }

        merged["USD"] = 1.00m;
        _table = new RateTable(merged, DateTimeOffset.UtcNow, live);
    }
}
