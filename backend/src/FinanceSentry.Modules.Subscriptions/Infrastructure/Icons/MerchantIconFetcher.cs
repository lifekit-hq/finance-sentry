namespace FinanceSentry.Modules.Subscriptions.Infrastructure.Icons;

using FinanceSentry.Modules.Subscriptions.Application.Icons;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fetches <c>https://{host}/favicon.ico</c> and nothing else. The host is a curated
/// <c>merchant_brands.Domain</c>; it is still required to be a plain DNS name, and the request is
/// https only, redirects are followed by hand and only within the same host (https, default port),
/// the body is capped, and only raster image types are accepted (SVG can carry script, so it is
/// refused). Any failure is a null result, never an exception, so the caller caches a negative.
/// </summary>
public sealed class MerchantIconFetcher(HttpClient client, ILogger<MerchantIconFetcher> logger) : IMerchantIconFetcher
{
    public const string HttpClientName = "merchant-icons";

    /// <summary>Largest icon accepted, in bytes. A favicon is a few kilobytes; this is a ceiling, not a target.</summary>
    public const int MaxBytes = 65_536;

    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private const int MaxRedirects = 2;
    private const int ReadChunk = 8192;

    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/x-icon", "image/vnd.microsoft.icon", "image/jpeg", "image/gif", "image/webp",
    };

    public async Task<FetchedIcon?> FetchAsync(string host, CancellationToken ct)
    {
        if (!PublicAddressGuard.IsPlainHostName(host))
            return null;

        try
        {
            var uri = new Uri($"https://{host}/favicon.ico");
            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    var next = response.Headers.Location is { } location
                        ? (location.IsAbsoluteUri ? location : new Uri(uri, location))
                        : null;
                    if (next is null || !IsSameHostHttps(next, uri))
                        return null;
                    uri = next;
                    continue;
                }

                return response.IsSuccessStatusCode ? await ReadIconAsync(response, ct) : null;
            }

            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or IOException)
        {
            if (ct.IsCancellationRequested)
                throw;
            logger.LogDebug(ex, "Merchant icon fetch for {Host} failed.", host);
            return null;
        }
    }

    private static bool IsSameHostHttps(Uri next, Uri current) =>
        next.Scheme == Uri.UriSchemeHttps
        && next.IsDefaultPort
        && string.Equals(next.Host, current.Host, StringComparison.OrdinalIgnoreCase);

    private static async Task<FetchedIcon?> ReadIconAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var type = response.Content.Headers.ContentType?.MediaType;
        if (type is null || !AllowedTypes.Contains(type))
            return null;
        if (response.Content.Headers.ContentLength is > MaxBytes)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[ReadChunk];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.Length == 0 ? null : new FetchedIcon(type.ToLowerInvariant(), buffer.ToArray());
    }
}
