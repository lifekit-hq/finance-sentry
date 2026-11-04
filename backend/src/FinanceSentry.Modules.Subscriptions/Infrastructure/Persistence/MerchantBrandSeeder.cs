namespace FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence;

using FinanceSentry.Modules.Subscriptions.Application.Icons;
using FinanceSentry.Modules.Subscriptions.Domain;

/// <summary>
/// Idempotently seeds <c>merchant_brands</c> from <see cref="MerchantBrandSeedData"/>: only missing
/// keywords are inserted, so a runtime edit to the table is never clobbered.
/// </summary>
public static class MerchantBrandSeeder
{
    public static void Seed(SubscriptionsDbContext db)
    {
        var existing = db.MerchantBrands.Select(b => b.Keyword).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var toAdd = MerchantBrandSeedData.Entries
            .Where(e => !existing.Contains(e.Keyword))
            .Select(e => new MerchantBrand { Keyword = e.Keyword, Domain = e.Domain })
            .ToList();

        if (toAdd.Count == 0)
            return;

        db.MerchantBrands.AddRange(toAdd);
        db.SaveChanges();
    }
}
