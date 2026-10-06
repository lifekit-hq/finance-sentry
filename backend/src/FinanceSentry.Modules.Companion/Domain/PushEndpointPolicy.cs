namespace FinanceSentry.Modules.Companion.Domain;

/// <summary>Which push-service endpoints the server will store, and later POST to: https on the default port, no
/// userinfo, on a known browser push-service host. Everything else, including IP literals, localhost and internal
/// hosts, is refused. Extend <see cref="ExactHosts"/> or <see cref="HostSuffixes"/> to admit another push service.</summary>
public static class PushEndpointPolicy
{
    private static readonly string[] ExactHosts =
    [
        "fcm.googleapis.com",
        "updates.push.services.mozilla.com",
        "web.push.apple.com",
    ];

    private static readonly string[] HostSuffixes =
    [
        ".push.services.mozilla.com",
        ".notify.windows.com",
        ".push.apple.com",
    ];

    public static bool IsAllowed(string? endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || uri.UserInfo.Length > 0)
            return false;

        var host = uri.IdnHost.TrimEnd('.');
        return ExactHosts.Contains(host, StringComparer.OrdinalIgnoreCase)
            || HostSuffixes.Any(s => host.EndsWith(s, StringComparison.OrdinalIgnoreCase));
    }
}
