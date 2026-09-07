namespace FinanceSentry.Modules.BankSync.Infrastructure.Categorization;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Application.Services.CategoryMapping;

/// <inheritdoc />
public sealed class TransactionCategorizer(ICategoryResolver categoryResolver) : ITransactionCategorizer
{
    private readonly ICategoryResolver _categoryResolver = categoryResolver;

    /// <inheritdoc />
    /// <remarks>
    /// The ladder, and why each rung sits where it does:
    /// <list type="number">
    /// <item><b>merchant-keyword bridge</b> — the runtime-editable <c>merchant_keywords</c> table
    /// is the admin's override channel, so it keeps the last word on a wording (#582).</item>
    /// <item><b>loan / installment repayment</b> — Monobank books repayments under the
    /// wire-transfer MCC 4829 and would otherwise vanish into <c>TRANSFER_OUT</c>; unlike a
    /// keyword the rule also reaches the mortgage, whose description is a bare masked card
    /// number (#553).</item>
    /// <item><b>provider category</b> — a bank's own semantic classification beats the
    /// directional-prefix heuristic below it: a TrueLayer "To Go Sushi" classified
    /// <c>Restaurants</c> is food, not a transfer.</item>
    /// <item><b>directional transfer / savings jar</b> — "To …" / "Поповнення «…»" outranks the
    /// MCC map because Monobank tags jar operations with the charity MCC 8398, so the
    /// description is the only trustworthy signal that they are internal moves.</item>
    /// <item><b>MCC map</b> — the coarse catch-all for everything still unclaimed.</item>
    /// </list>
    /// Rungs 3 and 5 read structured signals that no single provider supplies both of, so their
    /// relative order is a statement of intent rather than a live tie-break.
    /// <para>
    /// A rung that resolves to <see cref="CategoryKeys.Uncategorized"/> has not claimed the
    /// transaction, so the ladder continues past it instead of storing the miss.
    /// </para>
    /// </remarks>
    public string? Categorize(
        CategorizationSignals signals,
        IReadOnlyList<ActiveInstallmentPlan> activeInstallmentPlans)
    {
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(activeInstallmentPlans);

        return _categoryResolver.TryResolveKeyword(signals.Description)
            ?? LoanRepaymentClassifier.Resolve(
                signals.TransactionType, signals.MerchantName, signals.Description,
                signals.Amount, signals.Mcc, activeInstallmentPlans)
            ?? Claimed(_categoryResolver.ResolveCanonicalKey(signals.ProviderCategory))
            ?? TransferDescriptionClassifier.Resolve(signals.Description)
            ?? Claimed(_categoryResolver.ResolveMcc(signals.Mcc));
    }

    private static string? Claimed(string categoryKey) =>
        categoryKey == CategoryKeys.Uncategorized ? null : categoryKey;
}
