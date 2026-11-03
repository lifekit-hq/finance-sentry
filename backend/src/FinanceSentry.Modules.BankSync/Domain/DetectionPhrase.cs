namespace FinanceSentry.Modules.BankSync.Domain;

/// <summary>
/// One piece of statement vocabulary the detection code matches against: a marker word, a
/// directional prefix, a "this is not a merchant" word. Rows are data, not C# constants, so a
/// provider's or a locale's wording is added by inserting a row instead of changing code.
///
/// What a row means is decided by its <see cref="Kind"/> (see <see cref="DetectionPhraseKinds"/>),
/// which also fixes how the phrase is compared (substring or prefix, which text, which case rule).
/// <see cref="Provider"/> and <see cref="Locale"/> say where the wording comes from; they describe
/// the row and do not gate it. <see cref="MccCondition"/> and <see cref="CategoryCondition"/> do
/// gate it: a row that sets one applies only when the transaction carries that MCC / category,
/// and a caller that has no such signal never applies it.
///
/// A shared reference table, like <see cref="MerchantKeyword"/>: not per-user, no owner filter.
/// Seeded idempotently at startup (missing rows only, so a runtime edit is never clobbered).
/// </summary>
public class DetectionPhrase
{
    /// <summary>Surrogate primary key. Rows of a kind are tried in ascending id order.</summary>
    public int Id { get; set; }

    /// <summary>One of <see cref="DetectionPhraseKinds"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The wording matched. Compared as the kind dictates (see <see cref="DetectionPhraseKinds"/>).</summary>
    public string Phrase { get; set; } = string.Empty;

    /// <summary>Bank or card network the wording comes from (e.g. <c>monobank</c>, <c>aib</c>); <c>null</c> when it is not provider-specific.</summary>
    public string? Provider { get; set; }

    /// <summary>Language of the wording (e.g. <c>uk</c>, <c>en</c>); <c>null</c> when it is not language-specific.</summary>
    public string? Locale { get; set; }

    /// <summary>When set, the row applies only to a transaction with exactly this MCC.</summary>
    public int? MccCondition { get; set; }

    /// <summary>When set, the row applies only to a transaction carrying exactly this category key.</summary>
    public string? CategoryCondition { get; set; }
}

/// <summary>
/// The kinds of <see cref="DetectionPhrase"/>. Each constant documents the comparison its consumer
/// makes, because the comparison is part of what a row means.
/// </summary>
public static class DetectionPhraseKinds
{
    /// <summary>Substring of a normalized merchant key (lowercase, ordinal) that marks it as no merchant: a transfer, a cash withdrawal.</summary>
    public const string NotAMerchant = "not_a_merchant";

    /// <summary>Substring of a transaction description (lowercase, ordinal) that marks an installment (розстрочка) repayment.</summary>
    public const string InstallmentMarker = "installment_marker";

    /// <summary>Substring of a description (ignore case) that marks a full installment payoff, the signal a plan is finished.</summary>
    public const string InstallmentPayoffMarker = "installment_payoff_marker";

    /// <summary>Prefix of a trimmed description (ignore case) that is stripped to recover the merchant of an installment repayment.</summary>
    public const string InstallmentMerchantPrefix = "installment_merchant_prefix";

    /// <summary>Marketplace tag (ignore case) that ends an installment description; the merchant is the text before it.</summary>
    public const string InstallmentMerchantTag = "installment_merchant_tag";

    /// <summary>Prefix of a trimmed description (ignore case) that is an installment repayment only on a given <see cref="DetectionPhrase.MccCondition"/> (Monobank books «Платіж &lt;merchant&gt;» on the wire-transfer MCC).</summary>
    public const string InstallmentTransferPrefix = "installment_transfer_prefix";

    /// <summary>Prefix of a description (ignore case, leading whitespace ignored) for an external incoming payment: income, not a transfer.</summary>
    public const string IncomePrefix = "income_prefix";

    /// <summary>Prefix of a description (ignore case, leading whitespace ignored) for an internal incoming transfer.</summary>
    public const string TransferInPrefix = "transfer_in_prefix";

    /// <summary>Prefix of a description (ignore case, leading whitespace ignored) for an internal outgoing transfer.</summary>
    public const string TransferOutPrefix = "transfer_out_prefix";

    /// <summary>Prefix of a description (ignore case, leading whitespace ignored) that marks a cash lodgement at an ATM; a credit carrying it is income.</summary>
    public const string CashLodgementMarker = "cash_lodgement_marker";

    /// <summary>The kinds whose consumer compares against lowercased text; their phrases are lowercased when a set is built.</summary>
    internal static readonly IReadOnlySet<string> LowercaseKinds =
        new HashSet<string>(StringComparer.Ordinal) { NotAMerchant, InstallmentMarker };
}
