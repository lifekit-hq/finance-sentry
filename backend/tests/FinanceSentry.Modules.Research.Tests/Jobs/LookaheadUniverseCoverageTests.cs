namespace FinanceSentry.Modules.Research.Tests.Jobs;

using FinanceSentry.Core.Interfaces;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>The coverage set joins the look-ahead universe without widening what the alert detectors alert on.</summary>
public sealed class LookaheadUniverseCoverageTests : CoverageDetectorFixture
{
    [Fact]
    public async Task Coverage_tickers_come_from_the_coverage_set_upper_cased_and_distinct()
    {
        var tickers = await Universe("mu", "MU", "avgo").CoverageTickersAsync(UserId);

        tickers.Should().BeEquivalentTo(["MU", "AVGO"]);
    }

    [Fact]
    public async Task Held_coverage_names_come_before_the_rest_which_follow_in_ticker_order()
    {
        Hold("NVDA", "MU");

        var ordered = await Universe("AAPL", "ZM", "MU", "AVGO", "NVDA").CoverageTickersByPriorityAsync(UserId);

        ordered.Should().Equal("MU", "NVDA", "AAPL", "AVGO", "ZM");
    }

    [Fact]
    public async Task A_coverage_ticker_naming_a_held_crypto_asset_is_left_out()
    {
        Crypto.Setup(c => c.GetHoldingsAsync(UserId, default))
            .ReturnsAsync([new CryptoHoldingSummary("SOL", 1m, 0m, 100m, DateTime.UtcNow, "binance")]);

        var tickers = await Universe("SOL", "MU").CoverageTickersAsync(UserId);

        tickers.Should().BeEquivalentTo(["MU"]);
    }

    [Fact]
    public async Task An_empty_coverage_set_is_an_empty_universe()
    {
        (await Universe().CoverageTickersAsync(UserId)).Should().BeEmpty();
        (await Universe().CoverageTickersByPriorityAsync(UserId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Coverage_names_do_not_widen_the_alert_universes()
    {
        Hold("NVDA");
        var universe = Universe("MU", "NVDA");

        (await universe.EarningsTickersAsync(UserId)).Should().BeEquivalentTo(["NVDA"]);
        (await universe.FilingTickersAsync(UserId)).Should().BeEquivalentTo(["NVDA"]);
        (await universe.HeldEquityTickersAsync(UserId)).Should().BeEquivalentTo(["NVDA"]);
        Brokerage.Verify(b => b.GetHoldingsAsync(UserId, default), Times.AtLeastOnce);
    }
}
