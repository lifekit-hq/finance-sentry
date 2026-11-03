namespace FinanceSentry.Modules.BankSync.Infrastructure.Categorization;

using FinanceSentry.Core.Domain;
using FinanceSentry.Modules.BankSync.Application.Services.DetectionPhrases;
using FinanceSentry.Modules.BankSync.Domain;

/// <summary>
/// Classifies ATM cash lodgements as income. AIB renders a cash lodgement made at an ATM with
/// the terminal marker <c>*ATMLDG</c> (the <c>cash_lodgement_marker</c> detection phrase) followed by the branch, e.g. <c>*ATMLDG CAPEL ST</c>. The
/// owner's call (#1239) is that these deposits are income, not a transfer or an unexplained credit.
///
/// Credits only: a debit is never a lodgement, and an ATM withdrawal carries a different marker,
/// so both are left to the rest of the ladder.
/// </summary>
public static class CashLodgementClassifier
{
    /// <summary>
    /// Returns <see cref="CategoryKeys.Income"/> for a credit whose description starts with the
    /// AIB ATM-lodgement marker, or <c>null</c> when it is anything else.
    /// </summary>
    public static string? Resolve(DetectionPhraseSet phrases, string? description, string? transactionType)
    {
        if (string.IsNullOrWhiteSpace(description)
            || !string.Equals(transactionType, "credit", StringComparison.OrdinalIgnoreCase))
            return null;

        var trimmed = description.TrimStart();
        foreach (var marker in phrases.Phrases(DetectionPhraseKinds.CashLodgementMarker))
        {
            if (trimmed.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                return CategoryKeys.Income;
        }
        return null;
    }
}
