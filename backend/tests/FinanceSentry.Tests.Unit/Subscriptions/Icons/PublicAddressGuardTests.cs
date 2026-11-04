namespace FinanceSentry.Tests.Unit.Subscriptions.Icons;

using System.Net;
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
