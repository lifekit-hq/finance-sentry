namespace FinanceSentry.Modules.BankSync.Infrastructure.Categorization;

using FinanceSentry.Core.Domain;

/// <summary>
/// Classifies AIB ATM cash lodgements as income. AIB renders a cash lodgement made at an ATM with
/// the terminal marker <c>*ATMLDG</c> followed by the branch, e.g. <c>*ATMLDG CAPEL ST</c>. The
/// owner's call (#1239) is that these deposits are income, not a transfer or an unexplained credit.
///
/// Credits only: a debit is never a lodgement, and an ATM withdrawal carries a different marker,
/// so both are left to the rest of the ladder.
/// </summary>
public static class CashLodgementClassifier
{
    private const string LodgementMarker = "*ATMLDG";

    /// <summary>
    /// Returns <see cref="CategoryKeys.Income"/> for a credit whose description starts with the
    /// AIB ATM-lodgement marker, or <c>null</c> when it is anything else.
    /// </summary>
    public static string? Resolve(string? description, string? transactionType)
    {
        if (string.IsNullOrWhiteSpace(description)
            || !string.Equals(transactionType, "credit", StringComparison.OrdinalIgnoreCase))
            return null;

        return description.TrimStart().StartsWith(LodgementMarker, StringComparison.OrdinalIgnoreCase)
            ? CategoryKeys.Income
            : null;
    }
}
