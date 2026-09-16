using FinanceSentry.Modules.CryptoSync.Infrastructure.RevolutX;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Tests.Unit.CryptoSync.RevolutX;

public class RevolutXTradeCursorTests
{
    [Fact]
    public void RoundTrips()
    {
        RevolutXTradeCursor.Parse(RevolutXTradeCursor.Format(1_789_000_000_123)).Should().Be(1_789_000_000_123);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v1:")]
    [InlineData("v1:-5")]
    [InlineData("v2:123")]
    [InlineData("USDT=43")]
    public void AnythingElse_IsANeverWalkedCursor(string? cursor)
    {
        RevolutXTradeCursor.Parse(cursor).Should().BeNull();
    }
}
