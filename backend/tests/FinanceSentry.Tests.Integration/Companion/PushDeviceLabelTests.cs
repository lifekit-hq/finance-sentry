namespace FinanceSentry.Tests.Integration.Companion;

using FinanceSentry.Modules.Companion.Application.Services;
using FluentAssertions;
using Xunit;

public class PushDeviceLabelTests
{
    [Theory]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 Chrome/126.0 Mobile Safari/537.36", "Chrome on Android")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_4 like Mac OS X) AppleWebKit/605.1.15 Version/18.4 Mobile/15E148 Safari/604.1", "Safari on iPhone")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:130.0) Gecko/20100101 Firefox/130.0", "Firefox on Windows")]
    [InlineData("curl/8.0", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Derives_a_short_label_from_the_user_agent(string? userAgent, string? expected)
        => PushDeviceLabel.FromUserAgent(userAgent).Should().Be(expected);
}
