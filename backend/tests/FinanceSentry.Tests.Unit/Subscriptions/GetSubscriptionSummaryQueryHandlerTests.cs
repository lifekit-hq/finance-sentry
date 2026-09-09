namespace FinanceSentry.Tests.Unit.Subscriptions;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Subscriptions.Application.Queries;
using FinanceSentry.Modules.Subscriptions.Domain;
using FinanceSentry.Modules.Subscriptions.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class GetSubscriptionSummaryQueryHandlerTests
{
    private const string UserId = "user-1";

    private static DetectedSubscription ActiveSub(decimal averageAmount, string currency, string cadence = "monthly") =>
        DetectedSubscription.Create(
            UserId,
            merchantNameNormalized: "svc",
            merchantNameDisplay: "Svc",
            cadence: cadence,
            averageAmount: averageAmount,
            lastKnownAmount: averageAmount,
            currency: currency,
            lastChargeDate: new DateOnly(2026, 7, 1),
            nextExpectedDate: new DateOnly(2026, 8, 1),
            occurrenceCount: 3,
            confidenceScore: 90,
            category: null);

    private static DetectedSubscription ActiveInstallment(
        decimal averageAmount,
        string currency,
        int? termCount = null,
        int occurrenceCount = 3,
        DateOnly? endDate = null)
    {
        var item = DetectedSubscription.Create(
            UserId,
            merchantNameNormalized: "rozetka",
            merchantNameDisplay: "Rozetka",
            cadence: "monthly",
            averageAmount: averageAmount,
            lastKnownAmount: averageAmount,
            currency: currency,
            lastChargeDate: new DateOnly(2026, 7, 1),
            nextExpectedDate: new DateOnly(2026, 8, 1),
            occurrenceCount: occurrenceCount,
            confidenceScore: 90,
            category: null,
            kind: SubscriptionKinds.Installment);

        if (termCount is not null || endDate is not null)
            item.SetTerm(termCount, endDate);

        return item;
    }

    private static GetSubscriptionSummaryQueryHandler BuildHandler(params DetectedSubscription[] active)
    {
        var repo = new Mock<IDetectedSubscriptionRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetActiveByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(active);
        repo.Setup(r => r.GetByUserIdAsync(UserId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(active);
        return new GetSubscriptionSummaryQueryHandler(repo.Object);
    }

    private static Task<Modules.Subscriptions.API.Responses.SubscriptionSummaryResponse> Run(
        params DetectedSubscription[] active) =>
        BuildHandler(active).Handle(new GetSubscriptionSummaryQuery(UserId), CancellationToken.None);

    [Fact]
    public async Task Handle_SeparatesSubscriptionsFromInstallments()
    {
        var result = await Run(
            ActiveSub(10m, "USD"),
            ActiveInstallment(100m, "USD", termCount: 24));

        result.Subscriptions.Monthly.Should().Be(10m);
        result.Subscriptions.ActiveCount.Should().Be(1);
        result.Installments.Monthly.Should().Be(100m);
        result.Installments.ActiveCount.Should().Be(1);
        result.Combined.Monthly.Should().Be(110m);
        result.Combined.ActiveCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_InstallmentNearingEnd_AnnualizesByRemainingPaymentsNotTwelve()
    {
        // 2 of 3 payments made — one left. A naive ×12 would report 1200 instead of 100.
        var result = await Run(ActiveInstallment(100m, "USD", termCount: 3, occurrenceCount: 2));

        result.Installments.Next12Months.Should().Be(100m);
        result.Installments.RemainingCommitment.Should().Be(100m);
    }

    [Fact]
    public async Task Handle_InstallmentLongerThanAYear_CapsNext12MonthsAtTwelvePayments()
    {
        // 20-payment plan, 4 made → 16 left: a year costs 12, the plan still owes 16.
        var result = await Run(ActiveInstallment(100m, "USD", termCount: 20, occurrenceCount: 4));

        result.Installments.Next12Months.Should().Be(1200m);
        result.Installments.RemainingCommitment.Should().Be(1600m);
    }

    [Fact]
    public async Task Handle_EndDatedInstallment_UsesMonthsUntilEndDate()
    {
        // Mortgage-style: last charge 2026-07, final payment 2027-07 → 12 payments left.
        var result = await Run(ActiveInstallment(
            100m, "USD", endDate: new DateOnly(2027, 7, 1)));

        result.Installments.Next12Months.Should().Be(1200m);
        result.Installments.RemainingCommitment.Should().Be(1200m);
        result.Installments.HasUnknownSchedule.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_InstallmentWithoutSchedule_AssumesItContinuesAndFlagsApproximate()
    {
        var result = await Run(ActiveInstallment(100m, "USD"));

        result.Installments.Next12Months.Should().Be(1200m);
        // Nothing is known to be owed, so the total stays out of the commitment figure.
        result.Installments.RemainingCommitment.Should().Be(0m);
        result.Installments.HasUnknownSchedule.Should().BeTrue();
        result.Combined.HasUnknownSchedule.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Subscriptions_AreOpenEnded_SoAnnualIsTwelveMonths()
    {
        var result = await Run(ActiveSub(10m, "USD"));

        result.Subscriptions.Next12Months.Should().Be(120m);
        result.Subscriptions.RemainingCommitment.Should().BeNull();
        result.Subscriptions.HasUnknownSchedule.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_CombinedRemainingCommitment_IsNull_BecauseSubscriptionsNeverEnd()
    {
        var result = await Run(
            ActiveSub(10m, "USD"),
            ActiveInstallment(100m, "USD", termCount: 24));

        result.Combined.RemainingCommitment.Should().BeNull();
    }

    [Fact]
    public async Task Handle_MixedCurrencies_NormalizesToUsdBeforeSumming()
    {
        // 500 UAH (~$12) + 10 EUR (~$10.80) must NOT be summed as bare 510.
        var result = await Run(
            ActiveSub(500m, "UAH"),
            ActiveSub(10m, "EUR"));

        // 500 * 0.024 + 10 * 1.08 = 12.00 + 10.80 = 22.80
        result.Subscriptions.Monthly.Should().Be(22.80m);
        result.Subscriptions.Next12Months.Should().Be(273.60m);
        result.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task Handle_InstallmentInForeignCurrency_ConvertsBeforeAnnualizing()
    {
        // ₴1000/mo, 3 of 5 paid → 2 left. 1000 * 0.024 * 2 = 48.
        var result = await Run(ActiveInstallment(1000m, "UAH", termCount: 5, occurrenceCount: 3));

        result.Installments.Monthly.Should().Be(24m);
        result.Installments.Next12Months.Should().Be(48m);
        result.Installments.RemainingCommitment.Should().Be(48m);
    }

    [Fact]
    public async Task Handle_AnnualCadence_DividesByTwelveThenConverts()
    {
        var result = await Run(ActiveSub(120m, "EUR", cadence: "annual"));

        // (120 / 12) * 1.08 = 10 * 1.08 = 10.80
        result.Subscriptions.Monthly.Should().Be(10.80m);
    }

    [Fact]
    public async Task Handle_RestatedCurrency_ConvertsAtTheNewRateNotTheOldOne()
    {
        // The merchant's billing moved from a UAH account to a EUR one, so detection restated
        // ₴500/mo as €10/mo on the row it already had. Both halves of that restatement have to
        // land: a row holding the new amount under the old unit is converted at 0.024 instead
        // of 1.08, reporting $0.24/mo for a subscription that costs $10.80.
        var moved = ActiveSub(500m, "UAH");
        moved.UpdateFromDetection(
            "Svc", 10m, 10m, "EUR",
            new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1),
            4, 90, null);

        var result = await Run(moved);

        result.Subscriptions.Monthly.Should().Be(10.80m);
        result.Subscriptions.Next12Months.Should().Be(129.60m);
    }

    [Fact]
    public async Task Handle_NoActiveItems_ReturnsZeroesInUsd()
    {
        var result = await Run();

        result.Subscriptions.Monthly.Should().Be(0m);
        result.Installments.Monthly.Should().Be(0m);
        result.Combined.Monthly.Should().Be(0m);
        result.Combined.ActiveCount.Should().Be(0);
        result.Currency.Should().Be("USD");
    }
}
