namespace FinanceSentry.Core.Utils;

/// <summary>
/// Joins the app's public origin onto a relative app path (<see cref="AlertAppPath"/>) to give the absolute link a
/// message outside the app can carry (the agent has no app host of its own). Null whenever either half is missing or
/// unusable, so a consumer omits the link instead of printing a relative or wrong one.
/// </summary>
public static class AppUrl
{
    private const string BulletMark = "• ";
    private const string LinkMark = " → ";

    /// <summary>The persisted alert message column (<c>Alert.Message</c>): a longer digest body fails the insert.</summary>
    public const int MaxDigestLength = 1000;

    public static string? For(string? publicBaseUrl, string? appPath)
    {
        if (string.IsNullOrWhiteSpace(publicBaseUrl)
            || !Uri.TryCreate(publicBaseUrl.Trim(), UriKind.Absolute, out var baseUri)
            || baseUri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        // A path inside this app only: a protocol-relative "//host" would send the reader off-site.
        if (appPath is not { Length: > 0 } path
            || !path.StartsWith('/')
            || path.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        return baseUri.GetLeftPart(UriPartial.Authority) + baseUri.AbsolutePath.TrimEnd('/') + path;
    }

    /// <summary>
    /// One digest bullet: the text, then the absolute link to its entity when there is one. Without a page or a public
    /// base URL the bullet stays plain text.
    /// </summary>
    public static string Bullet(string text, string? publicBaseUrl, string? appPath)
        => For(publicBaseUrl, appPath) is { } url ? $"{BulletMark}{text}{LinkMark}{url}" : BulletMark + text;

    /// <summary>
    /// The digest body: the lines joined by newlines. Over <paramref name="maxLength"/> it drops the link of the last
    /// linked bullet, then the one before it, until the body fits, so a long origin costs the lowest links and never a
    /// fact (or the whole alert, which the column would refuse).
    /// </summary>
    public static string Digest(IReadOnlyList<string> lines, int maxLength = MaxDigestLength)
    {
        var kept = lines.ToArray();
        var length = kept.Sum(l => l.Length) + Math.Max(0, kept.Length - 1);

        for (var i = kept.Length - 1; i >= 0 && length > maxLength; i--)
        {
            var at = kept[i].LastIndexOf(LinkMark, StringComparison.Ordinal);
            if (at < 0)
            {
                continue;
            }

            length -= kept[i].Length - at;
            kept[i] = kept[i][..at];
        }

        return string.Join('\n', kept);
    }
}
