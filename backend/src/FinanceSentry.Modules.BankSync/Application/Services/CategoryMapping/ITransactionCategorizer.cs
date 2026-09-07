namespace FinanceSentry.Modules.BankSync.Application.Services.CategoryMapping;

using FinanceSentry.Core.Interfaces;

/// <summary>
/// Everything the categorization ladder is allowed to read about one transaction. Providers fill
/// only the signals they actually have — Monobank carries an <see cref="Mcc"/> and no
/// <see cref="ProviderCategory"/>, TrueLayer the reverse — so the ladder is one order for all of
/// them rather than one order per provider.
/// </summary>
/// <param name="Description">Free text as the provider rendered it.</param>
/// <param name="MerchantName">Counterparty name where the provider names one, else null.</param>
/// <param name="TransactionType">"debit" / "credit"; null on legacy rows, read as an outflow.</param>
/// <param name="Amount">Always positive — direction lives in <paramref name="TransactionType"/>.</param>
/// <param name="Mcc">Card MCC, for providers that supply one.</param>
/// <param name="ProviderCategory">
/// The provider's own classification, ALREADY mapped to a canonical key by the caller (the
/// backfill maps a stored <c>SourceCategory</c> via <see cref="TrueLayerCategoryMapper.MapStored"/>
/// so it matches what ingest passed). Re-validated against the canonical key set, so a provider
/// wording nothing maps falls through to the next rule rather than being stored verbatim.
/// </param>
public sealed record CategorizationSignals(
    string? Description,
    string? MerchantName,
    string? TransactionType,
    decimal Amount,
    int? Mcc = null,
    string? ProviderCategory = null);

/// <summary>
/// The single ordered categorization ladder. Every path that decides a transaction's category —
/// Monobank ingest, TrueLayer ingest and the recategorization backfill — runs this and only this,
/// so a row cannot be categorized one way at ingest and re-categorized another way by the next
/// backfill (issue #553).
/// </summary>
public interface ITransactionCategorizer
{
    /// <summary>
    /// Runs the ladder and returns a canonical category key, or <c>null</c> when no rule claimed
    /// the transaction. Callers that must store a value substitute
    /// <see cref="FinanceSentry.Core.Domain.CategoryKeys.Uncategorized"/>; the backfill keeps the
    /// null so the row stays eligible for a provider re-fetch.
    /// </summary>
    /// <param name="signals">The transaction's signals.</param>
    /// <param name="activeInstallmentPlans">
    /// The user's active installment plans, read once per sync / per backfill run — the loan rule
    /// matches each row against them, so a per-row lookup would be one query per transaction.
    /// </param>
    string? Categorize(
        CategorizationSignals signals,
        IReadOnlyList<ActiveInstallmentPlan> activeInstallmentPlans);
}
