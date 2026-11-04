namespace FinanceSentry.Modules.Subscriptions.Application.Icons;

using FinanceSentry.Modules.Subscriptions.Domain;

/// <summary>The curated merchant table, read as an in-memory snapshot.</summary>
public interface IMerchantBrandCatalog
{
    /// <summary>The brand a merchant name belongs to, or null when none of the curated merchants matches.</summary>
    MerchantBrand? Match(string? merchantName);

    /// <summary>The curated row with this id, or null.</summary>
    MerchantBrand? Find(int brandId);
}
