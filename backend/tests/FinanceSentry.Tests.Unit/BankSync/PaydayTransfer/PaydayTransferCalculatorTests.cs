namespace FinanceSentry.Tests.Unit.BankSync.PaydayTransfer;

using FinanceSentry.Modules.BankSync.Application.Services;
using FluentAssertions;
using Xunit;

public class PaydayTransferCalculatorTests
{
    [Fact]
    public void Exact_multiple_of_the_step_suggests_itself_with_no_cushion()
    {
        var result = PaydayTransferCalculator.Calculate(30000.00m, 15000m, 50.00m, 50m);

        result.TotalUah.Should().Be(45000m);
        result.ExactEur.Should().Be(900.00m);
        result.SuggestedEur.Should().Be(900m);
        result.CushionEur.Should().Be(0m);
    }

    [Fact]
    public void Suggested_rounds_up_to_the_step_and_the_difference_is_the_cushion()
    {
        // 43800 / 50 = 876 exactly -> 900 at step 50, cushion 24.
        var result = PaydayTransferCalculator.Calculate(28800m, 15000m, 50m, 50m);

        result.ExactEur.Should().Be(876.00m);
        result.SuggestedEur.Should().Be(900m);
        result.CushionEur.Should().Be(24.00m);
    }

    [Fact]
    public void Exact_eur_is_rounded_to_cents_before_the_step_is_applied()
    {
        // 45000 / 45.00 = 1000 exactly; a rate that leaves a sub-cent remainder must not bump a whole step.
        var result = PaydayTransferCalculator.Calculate(30000m, 15000m, 45.00m, 50m);
        result.SuggestedEur.Should().Be(1000m);

        var fractional = PaydayTransferCalculator.Calculate(30000m, 15000.0001m, 45.00m, 50m);
        fractional.ExactEur.Should().Be(1000.00m);
        fractional.SuggestedEur.Should().Be(1000m);
    }

    [Fact]
    public void A_larger_step_rounds_further_up()
    {
        var result = PaydayTransferCalculator.Calculate(30000m, 15000m, 50m, 100m);

        result.SuggestedEur.Should().Be(900m);
        PaydayTransferCalculator.Calculate(30000m, 15500m, 50m, 100m).SuggestedEur.Should().Be(1000m);
    }

    [Fact]
    public void Nothing_owed_covers_only_the_outflow()
    {
        var result = PaydayTransferCalculator.Calculate(0m, 15000m, 50m, 50m);

        result.ExactEur.Should().Be(300.00m);
        result.SuggestedEur.Should().Be(300m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_rate_is_rejected(int rate)
    {
        var act = () => PaydayTransferCalculator.Calculate(1m, 1m, rate, 50m);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_non_positive_step_is_rejected()
    {
        var act = () => PaydayTransferCalculator.Calculate(1m, 1m, 50m, 0m);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
