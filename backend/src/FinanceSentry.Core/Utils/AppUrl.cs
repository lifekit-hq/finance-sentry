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
}
