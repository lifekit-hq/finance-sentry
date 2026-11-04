namespace FinanceSentry.Modules.Subscriptions.Application.Icons;

using FinanceSentry.Modules.Subscriptions.Domain;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public sealed class MerchantIconService(
    SubscriptionsDbContext db,
    IMerchantBrandCatalog catalog,
    IMerchantIconFetcher fetcher,
    TimeProvider clock) : IMerchantIconService
{
    /// <summary>A found icon is served from the cache this long before the host is asked again.</summary>
    public static readonly TimeSpan PositiveTtl = TimeSpan.FromDays(30);

    /// <summary>A host with no usable icon is not asked again for this long (short enough that an outage heals within the day).</summary>
    public static readonly TimeSpan NegativeTtl = TimeSpan.FromHours(6);

    public async Task<MerchantIconContent?> GetAsync(int brandId, CancellationToken ct)
    {
        var brand = catalog.Find(brandId);
        if (brand is null)
            return null;

        var now = clock.GetUtcNow();
        var cached = await db.MerchantIcons.FirstOrDefaultAsync(i => i.Domain == brand.Domain, ct);
        if (cached is not null && cached.ExpiresAt > now)
            return ToContent(cached);

        var fetched = await fetcher.FetchAsync(brand.Domain, ct);
        var entry = cached ?? new MerchantIcon { Domain = brand.Domain };
        entry.ContentType = fetched?.ContentType;
        entry.Content = fetched?.Content;
        entry.FetchedAt = now;
        entry.ExpiresAt = now + (fetched is null ? NegativeTtl : PositiveTtl);
        if (cached is null)
            db.MerchantIcons.Add(entry);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two first requests raced to insert the same host; the other one's entry stands and the
            // image we just fetched is just as good to serve.
            db.Entry(entry).State = EntityState.Detached;
        }

        return ToContent(entry);
    }

    public async Task<IReadOnlySet<string>> GetKnownMissingAsync(IReadOnlyCollection<string> domains, CancellationToken ct)
    {
        if (domains.Count == 0)
            return new HashSet<string>();

        var now = clock.GetUtcNow();
        var missing = await db.MerchantIcons.AsNoTracking()
            .Where(i => domains.Contains(i.Domain) && i.Content == null && i.ExpiresAt > now)
            .Select(i => i.Domain)
            .ToListAsync(ct);
        return missing.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static MerchantIconContent? ToContent(MerchantIcon entry) =>
        entry.Content is { } bytes && entry.ContentType is { } type ? new MerchantIconContent(type, bytes) : null;
}
