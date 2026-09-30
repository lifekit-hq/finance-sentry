namespace FinanceSentry.Gateway;

using System.Net;

/// <summary>
/// Which hops may set X-Forwarded-* for this host. Reads <c>ForwardedHeaders:KnownProxies</c> (IP addresses)
/// and <c>ForwardedHeaders:KnownNetworks</c> (CIDR ranges); when either is set, those replace the framework
/// defaults. When neither is set the defaults stay — loopback only — never an empty list, which the
/// forwarded-headers middleware would read as "trust every sender".
/// </summary>
public static class TrustedForwarders
{
    public const string KnownProxiesKey = "ForwardedHeaders:KnownProxies";
    public const string KnownNetworksKey = "ForwardedHeaders:KnownNetworks";

    public static void Apply(ForwardedHeadersOptions options, IConfiguration config)
    {
        var proxies = config.GetSection(KnownProxiesKey).Get<string[]>() ?? [];
        var networks = config.GetSection(KnownNetworksKey).Get<string[]>() ?? [];
        if (proxies.Length == 0 && networks.Length == 0)
            return;

        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var proxy in proxies)
            options.KnownProxies.Add(IPAddress.Parse(proxy));
        foreach (var network in networks)
            options.KnownIPNetworks.Add(IPNetwork.Parse(network));
    }
}
