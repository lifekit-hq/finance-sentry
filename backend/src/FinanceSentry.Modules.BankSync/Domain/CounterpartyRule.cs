namespace FinanceSentry.Modules.BankSync.Domain;

using FinanceSentry.Core.Domain;

/// <summary>
/// A single matching rule for a <see cref="Counterparty"/>.
/// A transaction matches if the specified field contains the pattern (case-insensitive).
/// </summary>
public class CounterpartyRule : Entity
{
    /// <summary>FK to the owning counterparty.</summary>
    public Guid CounterpartyId { get; set; }

    /// <summary>
    /// Which transaction field to test.
    /// "description_contains" — checks <c>Transaction.Description</c>.
    /// "merchant_name_contains" — checks <c>Transaction.MerchantName</c>.
    /// </summary>
    public string MatchType { get; set; } = string.Empty;

    /// <summary>Substring pattern (case-insensitive).</summary>
    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// Optional account-currency filter (ISO code, e.g. "EUR"). When set, the rule matches
    /// only transactions on accounts in that currency — and a currency-scoped match beats a
    /// generic one for the same transaction. This is what lets the same statement wording
    /// mean two different things: «Від: Оксана Лисенко» in UAH is rent (income), in EUR it
    /// is the user's own money coming back from a routing hop.
    /// </summary>
    public string? Currency { get; set; }

    /// <summary>Navigation back to owning counterparty.</summary>
    public Counterparty? Counterparty { get; set; }
}
