namespace FinanceSentry.Tests.Unit.Subscriptions;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Modules.Subscriptions.Application.Queries;
using FinanceSentry.Modules.Subscriptions.Domain;
using FinanceSentry.Modules.Subscriptions.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class GetInstallmentFxImpactQueryHandlerTests
{
    private const string UserId = "user-1";

    // Real NBU quotes: the hryvnia weakened from ~39.5/USD in May 2024 to ~44.5 today,
    // so a fixed UAH payment costs materially less in dollars than at signing.
    private const decimal UahPerUsdAtSigning = 39.5151m;
    private const decimal UahPerUsdNow = 44.5445m;

    private sealed class StubRates : IHistoricalExchangeRateService
    {
        private readonly DateOnly _switchOver;

        public StubRates(DateOnly switchOver) => _switchOver = switchOver;

        public Task<IReadOnlyDictionary<DateOnly, decimal>> GetDailySeriesAsync(
            string currency, DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            var series = new Dictionary<DateOnly, decimal>();
            for (var d = from; d <= to; d = d.AddDays(1))
                series[d] = 1m / (d < _switchOver ? UahPerUsdAtSigning : UahPerUsdNow);

            return Task.FromResult<IReadOnlyDictionary<DateOnly, decimal>>(series);
        }

        public Task<decimal?> GetPublishedRateAsync(string currency, DateOnly date, CancellationToken ct = default) =>
            Task.FromResult<decimal?>(null);
    }

    private static DetectedSubscription Installment(
        decimal monthly,
        string currency,
        DateOnly lastCharge,
        int occurrenceCount = 3,
        DateOnly? startDate = null,
        string kind = SubscriptionKinds.Installment)
    {
        var item = DetectedSubscription.Create(
            UserId,
            merchantNameNormalized: $"plan-{monthly}",
            merchantNameDisplay: "Іпотека",
            cadence: "monthly",
            averageAmount: monthly,
            lastKnownAmount: monthly,
            currency: currency,
            lastChargeDate: lastCharge,
            nextExpectedDate: lastCharge.AddMonths(1),
            occurrenceCount: occurrenceCount,
            confidenceScore: 100,
            category: null,
            kind: kind);

        if (startDate is not null)
            item.SetTerm(null, endDate: null, startDate: startDate);

        return item;
    }

    private static GetInstallmentFxImpactQueryHandler BuildHandler(
        DateOnly switchOver, params DetectedSubscription[] active)
    {
        var repo = new Mock<IDetectedSubscriptionRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetActiveByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(active);
        return new GetInstallmentFxImpactQueryHandler(repo.Object, new StubRates(switchOver));
    }

    [Fact]
    public async Task Handle_UsesUserSetStartDate_AsTheBaseline()
    {
        // Plan signed May 2024; detection only saw the last few charges.
        var plan = Installment(
            14060.96m, "UAH", new DateOnly(2026, 8, 11),
            occurrenceCount: 3, startDate: new DateOnly(2024, 5, 1));

        var handler = BuildHandler(new DateOnly(2026, 1, 1), plan);
        var result = await handler.Handle(new GetInstallmentFxImpactQuery(UserId), CancellationToken.None);

        var impact = result.Plans.Should().ContainSingle().Subject;
        impact.BaselineDate.Should().Be(new DateOnly(2024, 5, 1));
        impact.BaselineIsObserved.Should().BeFalse();
        impact.BaselineUnitsPerBase.Should().BeApproximately(UahPerUsdAtSigning, 0.001m);
        impact.CurrentUnitsPerBase.Should().BeApproximately(UahPerUsdNow, 0.001m);
    }

    [Fact]
    public async Task Handle_WeakerLocalCurrency_MakesAFixedPaymentCheaper()
    {
        var plan = Installment(
            14060.96m, "UAH", new DateOnly(2026, 8, 11), startDate: new DateOnly(2024, 5, 1));

        var handler = BuildHandler(new DateOnly(2026, 1, 1), plan);
        var result = await handler.Handle(new GetInstallmentFxImpactQuery(UserId), CancellationToken.None);

        var impact = result.Plans.Single();
        // 14060.96 / 39.5151 = 355.84 → 14060.96 / 44.5445 = 315.68
        impact.BaselineCost.Should().BeApproximately(355.84m, 0.05m);
        impact.CurrentCost.Should().BeApproximately(315.68m, 0.05m);
        impact.ChangeAmount.Should().BeLessThan(0m);
        impact.ChangePercent.Should().BeApproximately(-11.29m, 0.1m);
    }

    [Fact]
    public async Task Handle_WithoutStartDate_FallsBackToFirstObservedCharge_AndFlagsIt()
    {
        // 3 observed charges ending Aug 2026 → baseline Jun 2026, well after signing.
        var plan = Installment(14060.96m, "UAH", new DateOnly(2026, 8, 11), occurrenceCount: 3);

        var handler = BuildHandler(new DateOnly(2026, 1, 1), plan);
        var result = await handler.Handle(new GetInstallmentFxImpactQuery(UserId), CancellationToken.None);

        var impact = result.Plans.Single();
        impact.BaselineDate.Should().Be(new DateOnly(2026, 6, 11));
        impact.BaselineIsObserved.Should().BeTrue();
        // Both ends sit after the rate move, so it understates rather than inventing a change.
        impact.ChangePercent.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_IgnoresBaseCurrencyPlans_WhereTheRateCannotMove()
    {
        var handler = BuildHandler(
            new DateOnly(2026, 1, 1),
            Installment(100m, "USD", new DateOnly(2026, 8, 1)));

        var result = await handler.Handle(new GetInstallmentFxImpactQuery(UserId), CancellationToken.None);

        result.Plans.Should().BeEmpty();
        result.Points.Should().BeEmpty();
        result.CurrentCostTotal.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_IgnoresSubscriptions_OnlyInstallmentsAreRepaymentPlans()
    {
        var handler = BuildHandler(
            new DateOnly(2026, 1, 1),
            Installment(500m, "UAH", new DateOnly(2026, 8, 1), kind: SubscriptionKinds.Subscription));

        var result = await handler.Handle(new GetInstallmentFxImpactQuery(UserId), CancellationToken.None);

        result.Plans.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_TotalsAcrossPlans_AndBuildsAMonthlySeries()
    {
        var handler = BuildHandler(
            new DateOnly(2026, 1, 1),
            Installment(14060.96m, "UAH", new DateOnly(2026, 8, 11), startDate: new DateOnly(2024, 5, 1)),
            Installment(2339.95m, "UAH", new DateOnly(2026, 8, 5), startDate: new DateOnly(2026, 5, 1)));

        var result = await handler.Handle(new GetInstallmentFxImpactQuery(UserId), CancellationToken.None);

        result.Plans.Should().HaveCount(2);
        result.CurrentCostTotal.Should().BeApproximately(
            result.Plans.Sum(p => p.CurrentCost), 0.01m);

        // Series spans May 2024 → today inclusive; count is date-relative so the test
        // doesn't drift as calendar months pass.
        var seriesStart = new DateOnly(2024, 5, 1);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var expectedMonths = (today.Year - seriesStart.Year) * 12 + today.Month - seriesStart.Month + 1;
        result.Points.Should().HaveCount(expectedMonths);
        result.Points[0].Date.Should().Be(seriesStart);
        // The second plan only starts contributing from its own baseline, so the total
        // steps up then — the line tracks rate movement, not plans appearing.
        result.Points[0].MonthlyCost.Should().BeLessThan(result.Points[^1].MonthlyCost);
    }

    [Fact]
    public async Task Handle_NoInstallments_ReturnsEmptyRatherThanFetchingRates()
    {
        var handler = BuildHandler(new DateOnly(2026, 1, 1));

        var result = await handler.Handle(new GetInstallmentFxImpactQuery(UserId), CancellationToken.None);

        result.Plans.Should().BeEmpty();
        result.BaseCurrency.Should().Be("USD");
        result.ChangePercentTotal.Should().Be(0m);
    }
}
