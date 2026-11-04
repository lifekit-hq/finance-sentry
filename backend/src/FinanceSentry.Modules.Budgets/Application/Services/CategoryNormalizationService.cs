namespace FinanceSentry.Modules.Budgets.Application.Services;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Budgets.Domain;

public class CategoryNormalizationService : ICategoryNormalizationService
{
    private const string HouseholdLabel = "Household bills";

    public string Normalize(string? rawCategory)
    {
        if (string.IsNullOrWhiteSpace(rawCategory))
            return CategoryKeys.Uncategorized;

        var upper = rawCategory.Trim().ToUpperInvariant();
        if (CategoryTaxonomy.ValidKeys.Contains(upper) || upper == SpendLines.Household)
            return upper;

        if (CategoryTaxonomy.LegacyKeyMap.TryGetValue(rawCategory.Trim(), out var mapped))
            return mapped;

        return CategoryKeys.Uncategorized;
    }

    public string GetLabel(string categoryKey)
    {
        var normalized = Normalize(categoryKey);
        if (normalized == SpendLines.Household)
            return HouseholdLabel;

        return CategoryTaxonomy.CategoryLabels.TryGetValue(normalized, out var label)
            ? label
            : "Uncategorized";
    }
}
