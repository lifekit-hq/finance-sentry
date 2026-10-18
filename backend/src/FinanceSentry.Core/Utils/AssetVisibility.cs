namespace FinanceSentry.Core.Utils;

using FinanceSentry.Core.Interfaces;

/// <summary>
/// The single display-visibility rule for a balance row (docs/money-semantics.md, "Display visibility").
/// A hidden row still counts in every total; the rule only labels it. The threshold in
/// the preferences and <c>valueInBase</c> must be in the same currency.
/// </summary>
public static class AssetVisibility
{
    public const string Empty = "empty";
    public const string Small = "small";

    /// <param name="valueInBase">The row's value, null when unknown (unpriced or unconverted).</param>
    /// <param name="isOwed">A card or loan with anything owed: never hidden by the small rule. A negative value is owed too.</param>
    /// <returns><see cref="Empty"/>, <see cref="Small"/>, or null when the row stays visible.</returns>
    public static string? Classify(decimal? valueInBase, bool isOwed, UserAssetVisibility prefs)
    {
        if (valueInBase is not { } value)
            return null;
        if (value == 0m)
            return prefs.HideEmptyBalances ? Empty : null;
        return prefs.HideSmallBalances && !isOwed && value > 0m && value < prefs.SmallBalanceThreshold ? Small : null;
    }

    /// <summary>
    /// The person's preferences with the threshold converted once to USD, the currency the money readers sum in,
    /// so a row's USD value and the threshold compare like with like. Defaults when the person has none stored.
    /// </summary>
    public static async Task<UserAssetVisibility> ResolveInUsdAsync(
        IUserAssetVisibilityReader? visibilityReader, IUserBaseCurrencyReader? baseCurrencyReader, Guid userId, CancellationToken ct)
    {
        var prefs = visibilityReader is null ? null : await visibilityReader.GetAsync(userId, ct);
        prefs ??= new UserAssetVisibility(HideEmptyBalances: true, HideSmallBalances: false, SmallBalanceThreshold: 1m);
        var baseCurrency = CurrencyConverter.ResolveBase(
            baseCurrencyReader is null ? null : await baseCurrencyReader.GetAsync(userId, ct));
        return prefs with { SmallBalanceThreshold = CurrencyConverter.ToUsd(prefs.SmallBalanceThreshold, baseCurrency) };
    }

    /// <summary>
    /// Classifies a position held in <paramref name="quantity"/> units worth <paramref name="usdValue"/>. A position
    /// with a quantity but no USD value is unpriced: its value is unknown, so it is never hidden. A negative value
    /// (a short) is owed and never small.
    /// </summary>
    public static string? ClassifyPosition(decimal quantity, decimal usdValue, UserAssetVisibility prefs)
    {
        decimal? value = quantity == 0m ? 0m : usdValue == 0m ? null : usdValue;
        return Classify(value, isOwed: false, prefs);
    }
}
