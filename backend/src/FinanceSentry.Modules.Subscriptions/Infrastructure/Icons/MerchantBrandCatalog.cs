namespace FinanceSentry.Modules.Subscriptions.Infrastructure.Icons;

using FinanceSentry.Modules.Subscriptions.Application.Icons;
using FinanceSentry.Modules.Subscriptions.Domain;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Singleton catalog backed by an in-memory snapshot of <c>merchant_brands</c>, loaded lazily on
/// first use (thread-safe) the way <c>DetectionPhraseProvider</c> loads its table: an edited row
/// takes effect on the next API restart.
/// </summary>
public sealed class MerchantBrandCatalog(IServiceScopeFactory scopeFactory) : IMerchantBrandCatalog
{
    private readonly object _gate = new();
    private IReadOnlyList<MerchantBrand>? _brands;

    public MerchantBrand? Match(string? merchantName) => MerchantBrandMatcher.Match(Brands, merchantName);

    public MerchantBrand? Find(int brandId) => Brands.FirstOrDefault(b => b.Id == brandId);

    private IReadOnlyList<MerchantBrand> Brands
    {
        get
        {
            if (_brands is { } loaded)
                return loaded;

            lock (_gate)
            {
                return _brands ??= Load();
            }
        }
    }

    private List<MerchantBrand> Load()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SubscriptionsDbContext>();
        return db.MerchantBrands.AsNoTracking().OrderBy(b => b.Id).ToList();
    }
}
