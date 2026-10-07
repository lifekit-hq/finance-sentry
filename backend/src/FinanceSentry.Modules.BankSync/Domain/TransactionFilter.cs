namespace FinanceSentry.Modules.BankSync.Domain;

/// <summary>
/// Native amount bounds applied only to transactions on one of <see cref="AccountIds"/> —
/// one entry per currency group, so a USD-normalised bound can be translated to each
/// currency's native magnitude without ever comparing native amounts across currencies.
/// </summary>
public sealed record AccountAmountRange(IReadOnlyList<Guid> AccountIds, decimal? Min, decimal? Max);

/// <summary>
/// Composed transaction filter, shared by the global ledger query and the per-account query.
/// Every field is optional; leaving all of them null reproduces an unfiltered read.
/// </summary>
/// <param name="Categories">
/// Category keys, matched against the stored <c>MerchantCategory</c>. A null column counts as
/// <c>UNCATEGORIZED</c> — the key the statistics group it under.
/// </param>
/// <param name="CategoryTransactionIds">
/// Transactions that belong to one of <paramref name="Categories"/> by classification rather
/// than by their stored column — the computed <c>FAMILY_SUPPORT</c> bucket. Only read when
/// <paramref name="Categories"/> is set.
/// </param>
public sealed record TransactionFilter(
    IReadOnlyList<Guid>? AccountIds = null,
    IReadOnlyList<string>? Categories = null,
    DateTime? From = null,
    DateTime? To = null,
    string? TransactionType = null,
    string? Search = null,
    decimal? MinAmount = null,
    decimal? MaxAmount = null,
    IReadOnlyList<AccountAmountRange>? AmountRanges = null,
    IReadOnlyList<Guid>? CategoryTransactionIds = null);
