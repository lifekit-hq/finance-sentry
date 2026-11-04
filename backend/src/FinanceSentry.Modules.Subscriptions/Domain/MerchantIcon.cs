namespace FinanceSentry.Modules.Subscriptions.Domain;

/// <summary>
/// The server-side cache of one fetched icon, keyed by the <see cref="MerchantBrand.Domain"/> it
/// came from. A fetch that found nothing usable is cached too (<see cref="Content"/> null), so an
/// unreachable or icon-less host is asked again only after <see cref="ExpiresAt"/>, not on every
/// page load.
/// </summary>
public class MerchantIcon
{
    public string Domain { get; set; } = string.Empty;

    /// <summary>Media type of <see cref="Content"/>; null on a negative entry.</summary>
    public string? ContentType { get; set; }

    /// <summary>The image bytes; null on a negative entry (nothing usable was found).</summary>
    public byte[]? Content { get; set; }

    public DateTimeOffset FetchedAt { get; set; }

    /// <summary>When the entry stops being served and the host is asked again.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    public bool IsNegative => Content is null;
}
