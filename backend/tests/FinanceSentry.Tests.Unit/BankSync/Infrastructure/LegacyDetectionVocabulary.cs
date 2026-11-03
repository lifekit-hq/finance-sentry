namespace FinanceSentry.Tests.Unit.BankSync.Infrastructure;

using FinanceSentry.Core.Domain;

/// <summary>
/// The detection vocabulary exactly as it was hard-coded before it moved into
/// <c>bank_sync.detection_phrases</c>: a frozen, verbatim copy of the old constants and the
/// comparisons made with them. <see cref="DetectionPhraseParityTests"/> runs it and the table-driven
/// classes over the same inputs. Never edit it to follow the production code; if the two disagree, one
/// of them changed behaviour.
/// </summary>
internal static class LegacyDetectionVocabulary
{
    // --- SubscriptionDetectionAlgorithm.UnidentifiableNormalizedNames / IsUnidentifiableMerchant ---

    private static readonly string[] UnidentifiableNormalizedNames =
    [
        "unknown", "transfer", "top-up", "topup", "recharge", "withdrawal", "atm", "cash", "переказ",
    ];

    public static bool IsUnidentifiableMerchant(string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized)) return true;
        if (normalized.StartsWith("mobile top-up ", StringComparison.Ordinal)) return false;

        foreach (var marker in UnidentifiableNormalizedNames)
        {
            if (normalized.Contains(marker, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    // --- InstallmentPlanRecognizer ---

    private const int InstallmentMcc = 4829;

    private static readonly string[] DescriptionMarkers =
    [
        "погашення наступного платежу",
        "повне погашення",
        "щомісячний платіж",
        "monomarket",
        "розстроч",
        "оплата частинами",
        "покупка частинами",
        "частинами",
        "installment",
    ];

    private static readonly string[] MerchantPrefixes =
    [
        "погашення наступного платежу",
        "повне погашення",
        "щомісячний платіж",
        "платіж",
    ];

    public static bool IsInstallmentDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return false;

        var lowered = description.ToLowerInvariant();
        foreach (var marker in DescriptionMarkers)
        {
            if (lowered.Contains(marker, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    public static bool IsInstallmentTransaction(string? description, int? mcc)
    {
        if (IsInstallmentDescription(description)) return true;
        if (mcc == InstallmentMcc && description is not null &&
            description.Trim().StartsWith("Платіж ", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return false;
    }

    public static bool IsInstallmentPayoff(string? description) =>
        description is not null &&
        description.Contains("повне погашення", StringComparison.OrdinalIgnoreCase);

    public static string ExtractMerchant(string description)
    {
        var text = description.Trim();

        foreach (var prefix in MerchantPrefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                text = text[prefix.Length..].Trim();
                break;
            }
        }

        var tagIdx = text.IndexOf("monomarket", StringComparison.OrdinalIgnoreCase);
        if (tagIdx >= 0)
            text = text[..tagIdx];

        return text.Trim(' ', '-', '\t');
    }

    // --- CashLodgementClassifier ---

    private const string LodgementMarker = "*ATMLDG";

    public static string? ResolveCashLodgement(string? description, string? transactionType)
    {
        if (string.IsNullOrWhiteSpace(description)
            || !string.Equals(transactionType, "credit", StringComparison.OrdinalIgnoreCase))
            return null;

        return description.TrimStart().StartsWith(LodgementMarker, StringComparison.OrdinalIgnoreCase)
            ? CategoryKeys.Income
            : null;
    }

    // --- TransferDescriptionClassifier ---

    private static readonly string[] IncomePrefixes = ["Payment from "];
    private static readonly string[] OutgoingPrefixes = ["To ", "Поповнення "];
    private static readonly string[] IncomingPrefixes = ["From ", "З банки "];

    public static string? ResolveTransferDescription(string? description)
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
