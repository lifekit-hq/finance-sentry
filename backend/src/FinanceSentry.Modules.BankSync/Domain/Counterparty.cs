namespace FinanceSentry.Modules.BankSync.Domain;

using FinanceSentry.Core.Domain;

/// <summary>
/// A named person or entity whose transactions with the user require flow reclassification
/// (e.g. family members whose rent credits are income and support debits are expense).
/// UserId = Guid.Empty marks a system-default counterparty that applies to all users.
/// </summary>
public class Counterparty : Entity
{
    /// <summary>
    /// Owning user. Guid.Empty = applies to every user (system default).
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>Display name shown in analytics (e.g. "Людмила Сичова (Мама)").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Semantic role deciding where each direction of the movement lands — one of
    /// <see cref="FlowRoles"/> ("family_support" seeded by M011, "investment" by M012).
    /// </summary>
    public string FlowRole { get; set; } = string.Empty;

    /// <summary>Navigation: match rules for this counterparty.</summary>
    public ICollection<CounterpartyRule> Rules { get; set; } = [];

    /// <summary>
    /// The monthly amount this counterparty is expected to send (e.g. rent), in
    /// <see cref="ExpectedMonthlyInflowCurrency"/>. Null when no expectation is configured — the
    /// pair is always set or cleared together. See docs/money-semantics.md §5.1.
    /// </summary>
    public decimal? ExpectedMonthlyInflowAmount { get; set; }

    /// <summary>ISO 4217 currency of <see cref="ExpectedMonthlyInflowAmount"/>, or null.</summary>
    public string? ExpectedMonthlyInflowCurrency { get; set; }
}
