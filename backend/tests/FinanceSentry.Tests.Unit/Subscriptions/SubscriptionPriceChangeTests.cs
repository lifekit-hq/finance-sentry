namespace FinanceSentry.Tests.Unit.Subscriptions;

using FinanceSentry.Modules.Subscriptions.Domain;
using FluentAssertions;
using Xunit;

/// <summary>
/// What counts as a price change between two consecutive charges (1217): a real move in the same currency on a later
/// charge, past the rounding tolerance.
/// </summary>
public class SubscriptionPriceChangeTests
{
    private static readonly DateOnly Previous = new(2026, 9, 15);
    private static readonly DateOnly Next = new(2026, 10, 15);
    private static readonly Guid SubscriptionId = Guid.NewGuid();

    private static SubscriptionPriceChange? Compare(
        decimal oldAmount, decimal newAmount, string oldCurrency = "EUR", string newCurrency = "EUR",
        DateOnly? newDate = null) =>
        SubscriptionPriceChange.TryCreate(
            "user", SubscriptionId, oldAmount, oldCurrency, Previous, newAmount, newCurrency, newDate ?? Next);

    [Fact]
    public void Increase_RecordsOldNewPercentAndChargeDate()
    {
        var change = Compare(9.99m, 12.99m)!;

        change.Should().NotBeNull();
        change.SubscriptionId.Should().Be(SubscriptionId);
        change.UserId.Should().Be("user");
        change.OldAmount.Should().Be(9.99m);
        change.NewAmount.Should().Be(12.99m);
        change.Currency.Should().Be("EUR");
        change.ChangePercent.Should().Be(30.03m);
        change.ChargeDate.Should().Be(Next);
        change.IsIncrease.Should().BeTrue();
    }

    [Fact]
    public void Decrease_IsRecordedWithANegativePercent()
    {
        var change = Compare(12.99m, 9.99m)!;

        change.Should().NotBeNull();
        change.ChangePercent.Should().Be(-23.09m);
        change.IsIncrease.Should().BeFalse();
    }

    [Theory]
    [InlineData(10.00, 10.00)]
    [InlineData(10.00, 11.00)]   // exactly the tolerance: still the same price
    [InlineData(10.00, 9.00)]
    [InlineData(9.99, 10.01)]    // rounding
    [InlineData(9.20, 9.55)]     // a USD price charged to a EUR account moving with the exchange rate
    [InlineData(80.00, 84.50)]   // a metered bill the detection variance gate admits
    [InlineData(84.50, 79.00)]
    [InlineData(79.00, 86.00)]
    public void WithinTolerance_RecordsNothing(double oldAmount, double newAmount) =>
        Compare((decimal)oldAmount, (decimal)newAmount).Should().BeNull();

    [Fact]
    public void JustPastTolerance_IsAChange() =>
        Compare(10.00m, 11.01m).Should().NotBeNull();

    [Fact]
    public void ADifferentCurrency_IsNotComparable() =>
        Compare(10.99m, 13.49m, oldCurrency: "GBP", newCurrency: "EUR").Should().BeNull();

    [Fact]
    public void CurrencyCaseIsNotADifference() =>
        Compare(9.99m, 12.99m, oldCurrency: "eur", newCurrency: "EUR").Should().NotBeNull();

    [Theory]
    [InlineData(0)]    // the same day
    [InlineData(-30)]  // an older charge than the one already held
    public void ANewChargeThatIsNotLater_RecordsNothing(int dayOffset) =>
        Compare(9.99m, 12.99m, newDate: Previous.AddDays(dayOffset)).Should().BeNull();

    [Theory]
    [InlineData(0, 12.99)]
    [InlineData(9.99, 0)]
    public void ANonPositiveAmount_RecordsNothing(double oldAmount, double newAmount) =>
        Compare((decimal)oldAmount, (decimal)newAmount).Should().BeNull();

    [Fact]
    public void UpdateFromDetection_ReturnsTheChangeAgainstTheChargeTheRowHeld()
    {
        var row = DetectedSubscription.Create(
            "user", "netflix", "Netflix", "monthly", 9.99m, 9.99m, "EUR",
            Previous, Previous.AddMonths(1), occurrenceCount: 3, confidenceScore: 3, category: null);

        var change = row.UpdateFromDetection(
            "Netflix", 12.99m, 12.99m, "EUR", Next, Next.AddMonths(1), 4, 4, null);

        change.Should().NotBeNull();
        change!.SubscriptionId.Should().Be(row.Id);
        change.OldAmount.Should().Be(9.99m);
        change.NewAmount.Should().Be(12.99m);
        row.LastKnownAmount.Should().Be(12.99m);
    }

    [Fact]
    public void UpdateFromDetection_WithTheSameCharge_ReturnsNothing()
    {
        var row = DetectedSubscription.Create(
            "user", "netflix", "Netflix", "monthly", 9.99m, 9.99m, "EUR",
            Previous, Previous.AddMonths(1), occurrenceCount: 3, confidenceScore: 3, category: null);

        row.UpdateFromDetection("Netflix", 12.99m, 12.99m, "EUR", Previous, Previous.AddMonths(1), 3, 3, null)
            .Should().BeNull("re-detecting the charge the row holds is not a new charge");
    }
}
