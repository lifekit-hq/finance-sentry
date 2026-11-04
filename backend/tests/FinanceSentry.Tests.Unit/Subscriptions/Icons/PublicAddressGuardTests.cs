namespace FinanceSentry.Tests.Unit.Subscriptions.Icons;

using System.Net;
using System.Net.Sockets;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Icons;
using FluentAssertions;
using Xunit;

public class PublicAddressGuardTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.5")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("::ffff:10.0.0.1")]
    public void IsPublic_PrivateOrSpecialAddress_ReturnsFalse(string address) =>
        PublicAddressGuard.IsPublic(IPAddress.Parse(address)).Should().BeFalse();

    [Theory]
    [InlineData("93.184.216.34")]
    [InlineData("172.32.0.1")]
    [InlineData("2606:2800:220:1:248:1893:25c8:1946")]
    public void IsPublic_GlobalAddress_ReturnsTrue(string address) =>
        PublicAddressGuard.IsPublic(IPAddress.Parse(address)).Should().BeTrue();

    [Fact]
    public async Task ConnectFirstPublicAsync_FirstAddressUnreachable_FallsThroughToNextPublicAddress()
    {
        var v6 = IPAddress.Parse("2606:2800:220:1:248:1893:25c8:1946");
        var v4 = IPAddress.Parse("93.184.216.34");
        var dialled = new List<IPAddress>();
        using var expected = new MemoryStream();

        var stream = await PublicAddressGuard.ConnectFirstPublicAsync("example.com", [v6, v4], 443, (endpoint, _) =>
        {
            dialled.Add(endpoint.Address);
            return endpoint.Address.Equals(v6)
                ? throw new SocketException((int)SocketError.NetworkUnreachable)
                : new ValueTask<Stream>(expected);
        }, CancellationToken.None);

        stream.Should().BeSameAs(expected);
        dialled.Should().Equal(v6, v4);
    }

    [Fact]
    public async Task ConnectFirstPublicAsync_PrivateAddresses_AreNeverDialled()
    {
        var dialled = new List<IPAddress>();
        using var expected = new MemoryStream();

        var stream = await PublicAddressGuard.ConnectFirstPublicAsync(
            "example.com",
            [IPAddress.Parse("10.0.0.5"), IPAddress.Parse("169.254.169.254"), IPAddress.Parse("93.184.216.34")],
            443,
            (endpoint, _) =>
            {
                dialled.Add(endpoint.Address);
                return new ValueTask<Stream>(expected);
            },
            CancellationToken.None);

        stream.Should().BeSameAs(expected);
        dialled.Should().Equal(IPAddress.Parse("93.184.216.34"));
    }

    [Fact]
    public async Task ConnectFirstPublicAsync_NoPublicAddress_ThrowsWithoutDialling()
    {
        var dialled = 0;

        var act = async () => await PublicAddressGuard.ConnectFirstPublicAsync(
            "internal.example.com",
            [IPAddress.Parse("127.0.0.1"), IPAddress.Parse("192.168.1.1")],
            443,
            (_, _) =>
            {
                dialled++;
                return new ValueTask<Stream>(Stream.Null);
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        dialled.Should().Be(0);
    }

    [Fact]
    public async Task ConnectFirstPublicAsync_EveryPublicAddressFails_ThrowsHttpRequestException()
    {
        var act = async () => await PublicAddressGuard.ConnectFirstPublicAsync(
            "example.com",
            [IPAddress.Parse("93.184.216.34")],
            443,
            (_, _) => throw new SocketException((int)SocketError.ConnectionRefused),
            CancellationToken.None);

        (await act.Should().ThrowAsync<HttpRequestException>()).WithInnerException<SocketException>();
    }

    [Theory]
    [InlineData("www.netflix.com")]
    [InlineData("epicentrk.ua")]
    [InlineData("a-b.example.org")]
    public void IsPlainHostName_DnsName_ReturnsTrue(string host) =>
        PublicAddressGuard.IsPlainHostName(host).Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]")]
    [InlineData("evil.com:8443")]
    [InlineData("evil.com/path")]
    [InlineData("user@evil.com")]
    [InlineData("-bad.example.com")]
    [InlineData("exa mple.com")]
    [InlineData("example..com")]
    public void IsPlainHostName_NotADnsName_ReturnsFalse(string? host) =>
        PublicAddressGuard.IsPlainHostName(host).Should().BeFalse();
}
