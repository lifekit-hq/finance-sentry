namespace FinanceSentry.Modules.BankSync.Application.Services.CategoryMapping;

/// <summary>
/// Resolves a raw provider category signal into a canonical category key, using the
/// runtime-editable <c>categories</c> / <c>mcc_categories</c> reference tables.
/// </summary>
public interface ICategoryResolver
{
    /// <summary>
    /// Resolves a Monobank/card MCC via the <c>mcc_categories</c> table.
    /// Returns UNCATEGORIZED when the MCC is null or unmapped.
    /// </summary>
    string ResolveMcc(int? mcc);

    /// <summary>
    /// Validates a stored raw category signal against the canonical key set (the signal is
    /// itself a canonical key when it came from a PFC-style source). Returns the canonical
    /// key when known, otherwise UNCATEGORIZED.
    /// </summary>
    string ResolveCanonicalKey(string? primary);

    /// <summary>
    /// Resolves a category from a free-text transaction description via the runtime-editable
    /// <c>merchant_keywords</c> table (longest keyword wins), returning null when nothing
    /// matches. This is one rung of
    /// <see cref="ITransactionCategorizer"/>'s ladder, not a categorization decision on its own.
    /// </summary>
    string? TryResolveKeyword(string? description);

    /// <summary>Drops the in-memory cache so the next resolve reloads from the database.</summary>
    void Refresh();
}
