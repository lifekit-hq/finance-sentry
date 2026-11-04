namespace FinanceSentry.Modules.Subscriptions.Application.Icons;

/// <summary>An image fetched from a curated host: its media type and bytes.</summary>
public sealed record FetchedIcon(string ContentType, byte[] Content);

/// <summary>
/// Fetches the icon of one curated host. The only caller passes a <c>MerchantBrand.Domain</c>; the
/// implementation still refuses anything that is not a plain public host name.
/// </summary>
public interface IMerchantIconFetcher
{
    /// <summary>The host's icon, or null when it has none the fetcher is willing to serve.</summary>
    Task<FetchedIcon?> FetchAsync(string host, CancellationToken ct);
}
