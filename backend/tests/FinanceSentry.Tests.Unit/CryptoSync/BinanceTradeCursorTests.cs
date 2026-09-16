using FinanceSentry.Modules.CryptoSync.Infrastructure.Binance;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Tests.Unit.CryptoSync;

public class BinanceTradeCursorTests
{
    private static readonly string[] Quotes = ["USDT", "USDC", "FDUSD", "BUSD"];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Parse_NeverWalked_IsEmpty(string? cursor)
    {
        BinanceTradeCursor.Parse(cursor, Quotes).Should().BeEmpty();
    }

    [Fact]
    public void Parse_LegacyBareNumber_AppliesToEveryPair()
    {
        // The value M004 migrates from CryptoHoldings.LastTradeId.
        BinanceTradeCursor.Parse("43", Quotes).Should().BeEquivalentTo(new Dictionary<string, long>
        {
            ["USDT"] = 43, ["USDC"] = 43, ["FDUSD"] = 43, ["BUSD"] = 43,
        });
    }

    [Fact]
    public void Parse_PerPairForm_RoundTripsThroughFormat()
    {
        var parsed = BinanceTradeCursor.Parse("USDC=7,USDT=1235", Quotes);

        parsed.Should().BeEquivalentTo(new Dictionary<string, long> { ["USDT"] = 1235, ["USDC"] = 7 });
        BinanceTradeCursor.Format(parsed).Should().Be("USDC=7,USDT=1235");
    }

    [Fact]
    public void Parse_IgnoresMalformedParts()
    {
        BinanceTradeCursor.Parse("USDT=12,=5,BUSD,USDC=x", Quotes)
            .Should().BeEquivalentTo(new Dictionary<string, long> { ["USDT"] = 12 });
    }

    [Fact]
    public void Format_NothingToResume_IsNull()
    {
        BinanceTradeCursor.Format(new Dictionary<string, long>()).Should().BeNull();
        BinanceTradeCursor.Format(new Dictionary<string, long> { ["USDT"] = 0 }).Should().BeNull();
    }
}
