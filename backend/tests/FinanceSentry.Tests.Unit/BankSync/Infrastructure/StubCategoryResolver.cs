namespace FinanceSentry.Tests.Unit.BankSync.Infrastructure;

using FinanceSentry.Core.Domain;
using FinanceSentry.Modules.BankSync.Application.Services.CategoryMapping;
using FinanceSentry.Modules.BankSync.Infrastructure.Categorization;

/// <summary>
/// Test double mirroring <c>CategoryResolver</c> semantics without a database: canonical keys
/// are validated against the real taxonomy; MCCs classify via the range rules.
/// </summary>
internal sealed class StubCategoryResolver : ICategoryResolver
{
    public static readonly StubCategoryResolver Instance = new();

    /// <summary>
    /// The REAL ordered ladder over this stub's reference data, so adapter and service tests run
    /// the production rule order rather than a second copy of it that could drift.
    /// </summary>
    public static readonly ITransactionCategorizer Categorizer = new TransactionCategorizer(Instance);

    public string ResolveMcc(int? mcc)
        => mcc is null ? CategoryKeys.Uncategorized : MccRangeClassifier.Classify(mcc.Value);

    // Mirrors the real resolver's validation: only a key that exists in the canonical taxonomy
    // survives, so an unmapped provider string falls through the ladder rather than being stored.
    public string ResolveCanonicalKey(string? primary)
    {
        if (string.IsNullOrWhiteSpace(primary))
            return CategoryKeys.Uncategorized;

        var normalized = primary.Trim().ToUpperInvariant();
        return CanonicalCategories.Definitions.Any(d => d.Key == normalized)
            ? normalized
            : CategoryKeys.Uncategorized;
    }

    // Minimal keyword set so adapter/service tests can exercise the keyword rung.
    public string? TryResolveKeyword(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;
        var h = description.ToLowerInvariant();
        if (h.Contains("lidl") || h.Contains("tesco"))
            return CategoryKeys.FoodAndDrink;
        if (h.Contains("amazon"))
            return CategoryKeys.GeneralMerchandise;
        if (h.Contains("погашення"))
            return CategoryKeys.LoanPayments;
        return null;
    }

    public void Refresh()
    {
    }
}
