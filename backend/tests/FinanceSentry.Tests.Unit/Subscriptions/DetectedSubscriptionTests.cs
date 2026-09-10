namespace FinanceSentry.Tests.Unit.Subscriptions;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Subscriptions.Domain;
using FluentAssertions;
using Xunit;

public class DetectedSubscriptionTests
{
    private static DetectedSubscription CreateInstallment(
        decimal amount = 14060.96m,
        DateOnly? lastCharge = null,
        int occurrenceCount = 3,
        string display = "Іпотека")
    {
        var charge = lastCharge ?? new DateOnly(2026, 8, 11);
        return DetectedSubscription.Create(
            "user-1",
            "516936",
            display,
            "monthly",
            amount,
            amount,
            "UAH",
            charge,
            charge.AddMonths(1),
            occurrenceCount,
            100,
            null,
            SubscriptionKinds.Installment);
    }

    [Fact]
    public void RemainingPayments_FromEndDate_CountsMonthsAfterLastCharge()
    {
        var mortgage = CreateInstallment(lastCharge: new DateOnly(2026, 8, 11));
        mortgage.SetTerm(null, new DateOnly(2036, 5, 1));

        // Sep 2026 … May 2036 inclusive.
        mortgage.RemainingPayments.Should().Be(117);
    }

    [Fact]
    public void RemainingPayments_EndDateTakesPrecedenceOverTerm()
    {
        var plan = CreateInstallment(occurrenceCount: 3);
        plan.SetTerm(20, new DateOnly(2027, 8, 1));

        plan.RemainingPayments.Should().Be(12);
    }

    [Fact]
    public void RemainingPayments_FromTerm_WhenNoEndDate()
    {
        var plan = CreateInstallment(occurrenceCount: 4);
        plan.SetTerm(20);

        plan.RemainingPayments.Should().Be(16);
    }

    [Fact]
    public void UpdateFromDetection_ChargeInFinalMonth_CompletesEndDatedPlan()
    {
        var mortgage = CreateInstallment();
        mortgage.SetTerm(null, new DateOnly(2036, 5, 1));
        mortgage.Status.Should().Be(SubscriptionStatus.Active);

        mortgage.UpdateFromDetection(
            "516936******4992", 14060.96m, 14060.96m, "UAH",
            new DateOnly(2036, 5, 11), new DateOnly(2036, 6, 11),
            13, 100, null, SubscriptionKinds.Installment);

        mortgage.Status.Should().Be(SubscriptionStatus.Completed);
    }

    [Fact]
    public void UpdateFromDetection_MaskedPanDisplay_DoesNotOverwriteReadableName()
    {
        var mortgage = CreateInstallment(display: "Іпотека");

        mortgage.UpdateFromDetection(
            "516936******4992", 14060.96m, 14060.96m, "UAH",
            new DateOnly(2026, 9, 11), new DateOnly(2026, 10, 11),
            4, 100, null, SubscriptionKinds.Installment);

        mortgage.MerchantNameDisplay.Should().Be("Іпотека");
    }

    [Fact]
    public void UpdateFromDetection_ReadableDisplay_StillUpdates()
    {
        var plan = CreateInstallment(display: "ТОВ Алло");

        plan.UpdateFromDetection(
            "ТОВ Алло - monomarket", 2339.95m, 2339.95m, "UAH",
            new DateOnly(2026, 9, 5), new DateOnly(2026, 10, 5),
            5, 100, null, SubscriptionKinds.Installment);

        plan.MerchantNameDisplay.Should().Be("ТОВ Алло - monomarket");
    }

    [Fact]
    public void UpdateFromDetection_BillingMovedToAnotherCurrency_RestatesTheUnitWithTheAmounts()
    {
        // Detection re-runs daily onto the row it already wrote. When the merchant's billing
        // moves to an account in another currency, the amounts it hands back are in that new
        // unit — so a row that kept its old Currency would label euros as hryvnia, and
        // GetSubscriptionSummaryQuery converts it through ToUsd at the wrong rate.
        var plan = CreateInstallment(amount: 14060.96m);
        plan.Currency.Should().Be("UAH");

        plan.UpdateFromDetection(
            "Іпотека", 320.50m, 320.50m, "EUR",
            new DateOnly(2026, 9, 11), new DateOnly(2026, 10, 11),
            4, 100, null, SubscriptionKinds.Installment);

        plan.Currency.Should().Be("EUR");
        plan.LastKnownAmount.Should().Be(320.50m);
    }
}
