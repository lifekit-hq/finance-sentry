namespace FinanceSentry.Core.Utils;

using System.Text.RegularExpressions;

/// <summary>
/// Recognizes masked card numbers ("400000******0002") and bare digit runs that banks
/// emit as the "merchant" of a card-to-card transfer. A recurring charge to such a
/// counterparty is a repayment obligation (loan/mortgage), not a service subscription,
/// and a masked PAN must never overwrite a human-readable display name.
/// </summary>
public static partial class MaskedPan
{
    [GeneratedRegex(@"^\d{4,8}\*+\d{2,6}$")]
    private static partial Regex MaskedPattern();

    public static bool IsLikely(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim();
        return MaskedPattern().IsMatch(trimmed)
            || (trimmed.Length >= 4 && trimmed.All(char.IsDigit));
    }
}
