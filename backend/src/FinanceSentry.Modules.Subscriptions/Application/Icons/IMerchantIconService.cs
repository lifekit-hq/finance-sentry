namespace FinanceSentry.Modules.Subscriptions.Application.Icons;

/// <summary>An icon ready to serve from our own origin.</summary>
public sealed record MerchantIconContent(string ContentType, byte[] Content);

public interface IMerchantIconService
{
    /// <summary>
    /// The icon of a curated brand, from the server-side cache or, when the entry is missing or
    /// expired, fetched once and cached. Null for an unknown brand or one with no usable icon.
    /// </summary>
    Task<MerchantIconContent?> GetAsync(int brandId, CancellationToken ct);

    /// <summary>Of these hosts, the ones whose cache entry says "no usable icon" and has not expired yet.</summary>
    Task<IReadOnlySet<string>> GetKnownMissingAsync(IReadOnlyCollection<string> domains, CancellationToken ct);
}
