namespace FinanceSentry.Modules.Research.Tests.Recommendations;

using FinanceSentry.Modules.Research.Domain.Recommendations;
using FluentAssertions;
using Xunit;

public sealed class RecommendationSizingTests
{
    private const decimal Book = 76_000m;

    private static RecommendationSizingInput Input(
        RecommendationAction action, decimal quantity = 2_962m, decimal positionUsd = 9_272m, decimal unitUsd = 3.13m,
        decimal? requested = null, decimal? cap = 0.095m, bool fractional = false)
        => new(action, quantity, positionUsd, Book, unitUsd, fractional, requested, cap);

    [Fact]
    public void Trim_without_a_size_takes_the_position_back_to_the_cap_in_whole_shares_rounded_up()
    {
        var size = RecommendationSizing.Size(Input(RecommendationAction.Trim))!;

        // Excess over 9.5% of 76,000 is 2,052; 2,052 / 3.13 = 655.6, rounded up so the trim reaches the cap.
        size.SizeShares.Should().Be(656m);
        size.SizeUsd.Should().Be(2_053.28m);
        size.ShareOfBookBeforePct.Should().Be(12.20m);
        size.ShareOfBookAfterPct.Should().Be(9.50m);
    }

    [Fact]
    public void Trim_within_the_cap_and_without_a_size_sizes_to_nothing()
        => RecommendationSizing.Size(Input(RecommendationAction.Trim, positionUsd: 5_000m)).Should().BeNull();

    [Fact]
    public void Trim_with_a_size_never_sells_more_than_is_held()
        => RecommendationSizing.Size(Input(RecommendationAction.Trim, quantity: 100m, requested: 50_000m))!
            .SizeShares.Should().Be(100m);

    [Fact]
    public void Exit_sells_the_whole_position_at_its_book_value()
    {
        var size = RecommendationSizing.Size(Input(RecommendationAction.Exit))!;

        size.SizeShares.Should().Be(2_962m);
        size.SizeUsd.Should().Be(9_272m);
        size.ShareOfBookAfterPct.Should().Be(0m);
    }

    [Fact]
    public void Add_rounds_whole_shares_down_so_it_stays_within_the_requested_amount()
    {
        var size = RecommendationSizing.Size(Input(RecommendationAction.Add, requested: 1_000m))!;

        size.SizeShares.Should().Be(319m);
        size.SizeUsd.Should().Be(998.47m);
        size.ShareOfBookAfterPct.Should().Be(13.51m);
    }

    [Fact]
    public void Add_without_a_size_sizes_to_nothing()
        => RecommendationSizing.Size(Input(RecommendationAction.Add)).Should().BeNull();

    [Fact]
    public void A_fractional_holding_sizes_to_six_decimals()
        => RecommendationSizing.Size(Input(RecommendationAction.Trim, quantity: 1m, unitUsd: 60_000m, requested: 1_000m, fractional: true))!
            .SizeShares.Should().Be(0.016667m);

    [Fact]
    public void Hold_moves_nothing()
        => RecommendationSizing.Size(Input(RecommendationAction.Hold)).Should().BeNull();

    [Theory]
    [InlineData(0, 3.13)]
    [InlineData(76000, 0)]
    public void Without_a_book_total_or_a_price_nothing_is_sized(decimal book, decimal unit)
        => RecommendationSizing.Size(new RecommendationSizingInput(
            RecommendationAction.Exit, 10m, 100m, book, unit, false, null, null)).Should().BeNull();

    [Fact]
    public void Excess_over_the_cap_is_null_without_a_cap_or_within_it()
    {
        RecommendationSizing.ExcessOverCapUsd(9_272m, Book, null).Should().BeNull();
        RecommendationSizing.ExcessOverCapUsd(5_000m, Book, 0.095m).Should().BeNull();
        RecommendationSizing.ExcessOverCapUsd(9_272m, Book, 0.095m).Should().Be(2_052m);
    }

    [Fact]
    public void Order_text_is_the_line_the_owner_types_at_the_broker()
    {
        RecommendationOrderText.Build(RecommendationAction.Trim, "GRAB", 656m, 3.13m, "USD", "IBKR")
            .Should().Be("SELL 656 GRAB / LIMIT 3.13 USD DAY / IBKR");
        RecommendationOrderText.Build(RecommendationAction.Add, "VWCE", 4m, 131.456m, "EUR", "IBKR")
            .Should().Be("BUY 4 VWCE / LIMIT 131.46 EUR DAY / IBKR");
        RecommendationOrderText.Build(RecommendationAction.Exit, "BTC", 0.25m, null, null, "Binance")
            .Should().Be("SELL 0.25 BTC / LIMIT DAY / Binance");
    }

    [Fact]
    public void Hold_or_nothing_to_trade_has_no_order_text()
    {
        RecommendationOrderText.Build(RecommendationAction.Hold, "GRAB", 10m, 3m, "USD", "IBKR").Should().BeNull();
        RecommendationOrderText.Build(RecommendationAction.Trim, "GRAB", 0m, 3m, "USD", "IBKR").Should().BeNull();
    }

    [Theory]
    [InlineData("ibkr", "IBKR")]
    [InlineData("revolutx", "Revolut X")]
    [InlineData("binance", "Binance")]
    [InlineData("", "your broker")]
    [InlineData("trading212", "Trading212")]
    public void Venue_is_the_broker_name_never_an_account_number(string provider, string label)
        => RecommendationOrderText.VenueLabel(provider).Should().Be(label);
}
