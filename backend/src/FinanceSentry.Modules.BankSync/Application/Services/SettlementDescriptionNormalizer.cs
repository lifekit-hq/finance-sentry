namespace FinanceSentry.Modules.BankSync.Application.Services;

using System.Text.RegularExpressions;

/// <summary>
/// Strips the artifacts a provider adds to a transaction's description when the transaction
/// settles, so the pending and posted versions of one movement still read as the same movement.
///
/// <para>
/// AIB rewrites the description on settlement: the pending feed sends
/// <c>*MOBI DENYS SYCHOV IE26090266947650 *MOBI DENYS SYCHOV</c> and the booked feed sends the
/// same line with a settlement date spliced in —
/// <c>*MOBI DENYS SYCHOV IE26090266947650 TxnDate: 02Sep2026 *MOBI DENYS SYCHOV</c>. Because
/// <see cref="PendingReconciler"/> keys on the description, the two never matched: the pending
/// row was never retired and the posted row landed beside it, double-counting the payment in
/// every outflow figure derived from it.
/// </para>
///
/// <para>
/// Deliberately narrow. The only fragment removed is the provider's own <c>TxnDate:</c> stamp,
/// whose shape (<c>02Sep2026</c>) no merchant name has. Anything wider would start merging
/// genuinely distinct transactions, which is the more expensive mistake: a lost transaction is
/// invisible, a duplicated one is at least loud.
/// </para>
/// </summary>
public static partial class SettlementDescriptionNormalizer
{
    /// <summary>
    /// Returns the description with settlement artifacts removed, whitespace collapsed, trimmed
    /// and lower-cased — the form to compare two descriptions by. Never returns null.
    /// </summary>
    public static string Normalize(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return string.Empty;

        var stripped = TxnDateStamp().Replace(description, " ");
        return Whitespace().Replace(stripped, " ").Trim().ToLowerInvariant();
    }

    // AIB's settlement stamp: "TxnDate: 02Sep2026". The day is 1-2 digits, the month a
    // three-letter abbreviation, the year four digits.
    [GeneratedRegex(@"\bTxnDate:\s*\d{1,2}[A-Za-z]{3}\d{4}\b", RegexOptions.IgnoreCase)]
    private static partial Regex TxnDateStamp();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
