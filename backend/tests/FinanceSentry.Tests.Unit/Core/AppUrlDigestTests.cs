namespace FinanceSentry.Tests.Unit.Core;

using FinanceSentry.Core.Utils;
using FluentAssertions;
using Xunit;

public sealed class AppUrlDigestTests
{
    private const string Base = "https://app.example.com";

    [Fact]
    public void Digest_KeepsEveryLink_WhenTheBodyFits()
    {
        var lines = new[] { AppUrl.Bullet("One", Base, "/a"), string.Empty, AppUrl.Bullet("Two", Base, "/b") };

        AppUrl.Digest(lines).Should().Be(string.Join('\n', lines));
    }

    [Fact]
    public void Digest_DropsLinksFromTheLastLinkedBulletBack_UntilTheBodyFits_KeepingEveryText()
    {
        var lines = new[]
        {
            AppUrl.Bullet("One", Base, "/a"),
            AppUrl.Bullet("Two", Base, "/b"),
            AppUrl.Bullet("Plain", Base, null),
            AppUrl.Bullet("Three", Base, "/c"),
        };
        var full = string.Join('\n', lines).Length;

        var body = AppUrl.Digest(lines, full - 1);

        body.Split('\n').Should().Equal(
            "• One → https://app.example.com/a",
            "• Two → https://app.example.com/b",
            "• Plain",
            "• Three");
    }

    [Fact]
    public void Digest_DropsEveryLink_AndNothingElse_WhenNothingFits()
    {
        var lines = new[] { AppUrl.Bullet("One", Base, "/a"), AppUrl.Bullet("Two", Base, "/b") };

        AppUrl.Digest(lines, 1).Should().Be("• One\n• Two");
    }
}
