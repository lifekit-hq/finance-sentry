namespace FinanceSentry.Modules.BankSync.Application.Services;

using FinanceSentry.Modules.BankSync.Domain;

/// <summary>
/// Decides which existing <b>pending</b> transactions have been superseded by a settled
/// (posted) version and should therefore be retired.
///
/// A pending transaction and its later posted counterpart hash differently — the pending
/// version hashes on its transaction date, the posted version on its posted date (see
/// <see cref="TransactionDeduplicationService"/>) — so deduplication keeps both. Without
/// reconciliation the pending row lingers forever as a stale duplicate. Once a posted row
/// exists for the same account + amount + description, the pending row is stale.
///
/// <para>
/// The description is compared through <see cref="SettlementDescriptionNormalizer"/> because a
/// provider may also rewrite the text on settlement — AIB splices a <c>TxnDate:</c> stamp into
/// it — and a raw comparison then misses the twin, leaving both rows active and the payment
/// counted twice.
/// </para>
/// </summary>
public static class PendingReconciler
{
    /// <summary>
    /// Returns the pending rows from <paramref name="existingActive"/> that now have a
    /// settled (posted) twin — among the existing rows or the batch just inserted.
    /// Only existing rows are considered for retirement; freshly inserted rows are current.
    /// </summary>
    public static IReadOnlyList<Transaction> SelectStalePending(
        IEnumerable<Transaction> existingActive,
        IEnumerable<Transaction> newlyInserted)
    {
        var existing = existingActive as IReadOnlyList<Transaction> ?? existingActive.ToList();

        var postedKeys = existing
            .Concat(newlyInserted)
            .Where(t => !t.IsPending)
            .Select(Key)
            .ToHashSet();

        if (postedKeys.Count == 0)
            return [];

        return existing
            .Where(t => t.IsPending && postedKeys.Contains(Key(t)))
            .ToList();
    }

    // Pending↔posted match key: same account, amount, and merchant text. Deliberately excludes
    // the date, which is exactly what differs between the two versions — and normalizes the
    // text, which some providers also rewrite on settlement.
    private static string Key(Transaction t) =>
        $"{t.AccountId}|{t.Amount:F2}|{SettlementDescriptionNormalizer.Normalize(t.Description)}";
}
