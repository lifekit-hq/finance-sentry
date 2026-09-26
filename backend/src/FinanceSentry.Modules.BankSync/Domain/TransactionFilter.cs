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
public sealed record TransactionFilter(
    IReadOnlyList<Guid>? AccountIds = null,
    IReadOnlyList<string>? Categories = null,
    DateTime? From = null,
    DateTime? To = null,
    string? TransactionType = null,
    string? Search = null,
    decimal? MinAmount = null,
    decimal? MaxAmount = null,
    IReadOnlyList<AccountAmountRange>? AmountRanges = null);
