namespace FinanceSentry.Modules.BankSync.Infrastructure.Categorization;

using FinanceSentry.Core.Domain;

/// <summary>
/// Classifies bank-transfer and income transactions from their description prefix, for providers
/// that give no structured category (TrueLayer) or whose MCC misleads (Monobank). Open-banking
/// feeds render peer/account transfers with a directional prefix — "To &lt;name/account&gt;" for
/// outgoing, "From …" for incoming — which is a reliable signal that a merchant-keyword match is
/// not. TrueLayer distinguishes an EXTERNAL incoming payment ("Payment from &lt;party&gt;" — salary,
/// refunds, peer payments) from an internal own-account move (bare "From &lt;account&gt;", e.g.
/// "From Instant Access Savings"): the former is income, not a transfer, so it must NOT be
/// excluded from cash-flow. Monobank «банка» (savings-jar) operations are the same shape:
/// "Поповнення «Jar»" (top-up, outgoing) and "З банки «Jar»" (withdrawal, incoming). Monobank
/// tags them with the charity MCC 8398, so the description is the only trustworthy signal that
/// they are internal transfers and not government/charity spend.
///
/// Deliberately conservative: only the directional prefixes match, and the trailing space is
/// required so "Tobacco"/"Tommy" don't trip the "To" branch. This is one rung of
/// <see cref="Application.Services.CategoryMapping.ITransactionCategorizer"/>'s ladder, which
/// runs merchant-keyword matching above it, so "To Go Sushi" still lands as food rather than a
/// transfer.
/// </summary>
public static class TransferDescriptionClassifier
{
    // Checked before IncomingPrefixes: an external "Payment from <party>" is income, not an
    // internal transfer, so it stays in cash-flow instead of being excluded like a bare "From".
    private static readonly string[] IncomePrefixes = ["Payment from "];
    private static readonly string[] OutgoingPrefixes = ["To ", "Поповнення "];
    private static readonly string[] IncomingPrefixes = ["From ", "З банки "];

    /// <summary>
    /// Returns <see cref="CategoryKeys.Income"/> for an external incoming payment,
    /// <see cref="CategoryKeys.TransferOut"/> / <see cref="CategoryKeys.TransferIn"/> for a
    /// directional internal-transfer description, or <c>null</c> when it is neither.
    /// </summary>
    public static string? Resolve(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;

        var d = description.TrimStart();

        foreach (var prefix in IncomePrefixes)
        {
            if (d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return CategoryKeys.Income;
        }

        foreach (var prefix in IncomingPrefixes)
        {
            if (d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return CategoryKeys.TransferIn;
        }

        foreach (var prefix in OutgoingPrefixes)
        {
            if (d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return CategoryKeys.TransferOut;
        }

        return null;
    }
}
