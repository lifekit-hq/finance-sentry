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
/// exists for the same account + amount + description, shortly after it, the pending row is stale.
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
    /// How long after a pending row's date its settled twin may still land. Providers settle within
    /// days (a card hold clears in one to three, a bank booking in up to a working week), so a posted
    /// row any later than this is a different payment, not the pending row's successor.
    /// </summary>
    public const int SettlementWindowDays = 7;

    /// <summary>
    /// Returns the pending rows from <paramref name="existingActive"/> that now have a
    /// settled (posted) twin — among the existing rows or the batch just inserted.
    /// Only existing rows are considered for retirement; freshly inserted rows are current.
    ///
    /// <para>
    /// The match is date-aware and one-to-one: a posted row is the twin of a pending row only when it
    /// carries the same account, amount and text, is dated on or after the pending row (up to
    /// <see cref="SettlementWindowDays"/> later), and no other pending row has already taken it.
    /// Without the date bound, last month's posted rent retired this month's pending rent, whose own
    /// settled copy then hashed like the retired row and was dropped as a duplicate (#1229).
    /// </para>
    /// </summary>
    /// <param name="existingActive">The account's active rows.</param>
    /// <param name="newlyInserted">The batch just inserted.</param>
    /// <param name="alreadySettled">
    /// Rows that were pending rows themselves and settled in place in this run. They are the settled
    /// form of a pending row, not a successor to another one, so they never retire a pending row.
    /// </param>
    /// <param name="retiredPending">
    /// The account's pending rows an earlier run already retired. Each keeps the posted row that retired
    /// it, so the one-to-one claim holds across syncs: that posted row cannot retire the next pending row.
    /// </param>
    public static IReadOnlyList<Transaction> SelectStalePending(
        IEnumerable<Transaction> existingActive,
        IEnumerable<Transaction> newlyInserted,
        IReadOnlyCollection<Transaction>? alreadySettled = null,
        IEnumerable<Transaction>? retiredPending = null)
    {
        var existing = existingActive as IReadOnlyList<Transaction> ?? existingActive.ToList();

        var postedByKey = existing
            .Concat(newlyInserted)
            .Where(t => !t.IsPending && alreadySettled?.Contains(t) != true)
            .GroupBy(Key)
            .ToDictionary(g => g.Key, g => g.OrderBy(EffectiveDate).ToList());

        if (postedByKey.Count == 0)
            return [];

        var claimed = new HashSet<Transaction>();

        // Oldest pending first, each taking the earliest posted row still free in its window, so the
        // pairing does not depend on the order the rows were read in. Retired rows go first: they
        // already own their twin.
        Transaction? ClaimTwin(Transaction pending)
        {
            if (!postedByKey.TryGetValue(Key(pending), out var candidates))
                return null;

            var earliest = pending.TransactionDate.Date;
            var latest = earliest.AddDays(SettlementWindowDays);
            var twin = candidates.FirstOrDefault(p =>
                !claimed.Contains(p) && EffectiveDate(p) >= earliest && EffectiveDate(p) <= latest);
            if (twin is not null)
                claimed.Add(twin);
            return twin;
        }

        foreach (var retired in (retiredPending ?? []).OrderBy(t => t.TransactionDate))
            ClaimTwin(retired);

        var stale = new List<Transaction>();
        foreach (var pending in existing.Where(t => t.IsPending).OrderBy(t => t.TransactionDate))
        {
            if (ClaimTwin(pending) is not null)
                stale.Add(pending);
        }

        return stale;
    }

    // The day a posted row settled on: its posted date, else the date it carries.
    private static DateTime EffectiveDate(Transaction t) => (t.PostedDate ?? t.TransactionDate).Date;

    // Pending↔posted match key: same account, amount, and merchant text. The date is compared
    // separately, in a window, because it is exactly what differs between the two versions — and the
    // text is normalized, which some providers also rewrite on settlement.
    private static string Key(Transaction t) =>
        $"{t.AccountId}|{t.Amount:F2}|{SettlementDescriptionNormalizer.Normalize(t.Description)}";
}
