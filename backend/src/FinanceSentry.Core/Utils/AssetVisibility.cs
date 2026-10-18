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
}
