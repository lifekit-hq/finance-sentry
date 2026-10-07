namespace FinanceSentry.Modules.Companion.Application.Services;

/// <summary>
/// Turns an event's relative <see cref="Domain.CompanionEvent.AppPath"/> into the absolute link the agent wake and the
/// MCP event carry (the agent has no app host of its own). Null whenever either half is missing or unusable, so a
/// consumer omits the link instead of printing a relative or wrong one.
/// </summary>
public static class CompanionAppUrl
{
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
}
