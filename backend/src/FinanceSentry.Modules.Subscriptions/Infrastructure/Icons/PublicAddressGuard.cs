namespace FinanceSentry.Modules.Subscriptions.Infrastructure.Icons;

using System.Net;
using System.Net.Sockets;

/// <summary>
/// Keeps the icon fetcher on the public internet. The hosts it calls come from a table an operator
/// edits, so a bad row (or a DNS answer that points a public name at a private address) must not
/// reach loopback, the LAN, a link-local metadata endpoint or a container's neighbours.
/// </summary>
public static class PublicAddressGuard
{
    private const int MaxHostLength = 253;

    /// <summary>True for a plain DNS name: dotted labels of letters, digits and hyphens - no IP literal, port, path or user info.</summary>
    public static bool IsPlainHostName(string? host)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Length > MaxHostLength || !host.Contains('.'))
            return false;
        if (IPAddress.TryParse(host, out _))
            return false;

        foreach (var label in host.Split('.'))
        {
            if (label.Length is 0 or > 63 || label[0] == '-' || label[^1] == '-')
                return false;
            if (!label.All(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-'))
                return false;
        }

        return true;
    }

    /// <summary>True when the address is globally routable: not loopback, private, link-local, multicast, unspecified or carrier-grade NAT.</summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return false;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.IsIPv6UniqueLocal)
                return false;
            return true;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var b = address.GetAddressBytes();
        return !(b[0] == 10                                  // 10.0.0.0/8
                 || (b[0] == 172 && b[1] is >= 16 and <= 31) // 172.16.0.0/12
                 || (b[0] == 192 && b[1] == 168)             // 192.168.0.0/16
                 || (b[0] == 169 && b[1] == 254)             // 169.254.0.0/16 link-local, cloud metadata
                 || (b[0] == 100 && b[1] is >= 64 and <= 127) // 100.64.0.0/10 carrier-grade NAT
                 || b[0] == 0                                // 0.0.0.0/8
                 || b[0] >= 224);                            // multicast and reserved
    }

    /// <summary>
    /// Connects to the first public address the name resolves to. Used as the handler's connect
    /// callback, so the check applies to the address actually dialled (no gap between a check and a
    /// second DNS lookup) and to every redirect hop.
    /// </summary>
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
        var target = addresses.FirstOrDefault(IsPublic)
                     ?? throw new HttpRequestException($"No public address for {context.DnsEndPoint.Host}.");

        var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(target, context.DnsEndPoint.Port), ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
