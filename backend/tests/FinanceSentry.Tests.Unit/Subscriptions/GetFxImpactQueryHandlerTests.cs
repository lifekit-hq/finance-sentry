namespace FinanceSentry.Tests.Unit.Subscriptions;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Modules.Subscriptions.Application.Queries;
using FinanceSentry.Modules.Subscriptions.Domain;
using FinanceSentry.Modules.Subscriptions.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class GetFxImpactQueryHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    // Real NBU quotes: the hryvnia weakened from ~39.5/USD in May 2024 to ~44.5 today,
    // so a fixed UAH payment costs materially less in dollars than at signing.
    private const decimal UahPerUsdAtSigning = 39.5151m;
    private const decimal UahPerUsdNow = 44.5445m;
    private const decimal UsdPerEur = 1.10m;
    private const decimal UsdPerPlnAtSigning = 0.25m;
    private const decimal UsdPerPlnNow = 0.28m;

    private static readonly DateOnly Signing = new(2024, 5, 1);
    private static readonly DateOnly RateSwitch = new(2026, 1, 1);

    /// <summary>
    /// Publishes a quote for every day from well before the plans' start: UAH steps once at
    /// <see cref="RateSwitch"/>, EUR is flat, and a currency outside the map has no feed at all.
    /// </summary>
    private sealed class StubRates : IHistoricalExchangeRateService
    {
        public Task<IReadOnlyDictionary<DateOnly, decimal>> GetPublishedSeriesAsync(
            string currency, DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            var series = new Dictionary<DateOnly, decimal>();
            Func<DateOnly, decimal>? quote = currency.ToUpperInvariant() switch
            {
                "UAH" => d => 1m / (d < RateSwitch ? UahPerUsdAtSigning : UahPerUsdNow),
                "EUR" => _ => UsdPerEur,
                "PLN" => d => d < RateSwitch ? UsdPerPlnAtSigning : UsdPerPlnNow,
                _ => null,
            };

            if (quote is not null)
            {
                for (var d = from.AddDays(-CachingHistoricalExchangeRateService.PublishedLookbackDays); d <= to; d = d.AddDays(1))
                    series[d] = quote(d);
            }

            return Task.FromResult<IReadOnlyDictionary<DateOnly, decimal>>(series);
        }

        public Task<IReadOnlyDictionary<DateOnly, decimal>> GetDailySeriesAsync(
            string currency, DateOnly from, DateOnly to, CancellationToken ct = default) =>
            throw new NotSupportedException("the insight must not read the gap-filled series");

        public Task<decimal?> GetPublishedRateAsync(string currency, DateOnly date, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ConvertResult> ConvertToUsdAsync(
            decimal amount, string currency, DateOnly date, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private static DetectedSubscription Plan(
        decimal monthly,
        string currency,
        DateOnly lastCharge,
        int occurrenceCount = 3,
        DateOnly? startDate = null,
        int? termCount = null,
        string kind = SubscriptionKinds.Installment)
    {
        var item = DetectedSubscription.Create(
            UserId.ToString(),
            merchantNameNormalized: $"plan-{monthly}-{currency}",
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

        if (startDate is not null || termCount is not null)
            item.SetTerm(termCount, endDate: null, startDate: startDate);

        return item;
    }

    private static GetFxImpactQueryHandler BuildHandler(string baseCurrency, params DetectedSubscription[] active)
    {
        var repo = new Mock<IDetectedSubscriptionRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetActiveByUserIdAsync(UserId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(active);

        var baseCurrencyReader = new Mock<IUserBaseCurrencyReader>(MockBehavior.Strict);
        baseCurrencyReader.Setup(r => r.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(baseCurrency);

        return new GetFxImpactQueryHandler(repo.Object, new StubRates(), baseCurrencyReader.Object);
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    // ── the data gate ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("UAH", "USD", true)]
    [InlineData("UAH", "EUR", true)]
    [InlineData("PLN", "USD", true)]
    [InlineData("USD", "UAH", false)]
    [InlineData("EUR", "UAH", false)]
    [InlineData("usd", "UAH", false)]
    [InlineData("UAH", "UAH", false)]
    [InlineData("uah", "UAH", false)]
    [InlineData("", "USD", false)]
    public void IsForeign_ExcludesTheMajorsAndTheBaseCurrency(string currency, string baseCurrency, bool expected) =>
        GetFxImpactQueryHandler.IsForeign(currency, baseCurrency).Should().Be(expected);

    [Fact]
    public async Task Handle_NoPlans_ReturnsAnEmptyGate()
    {
        var result = await BuildHandler("USD").Handle(new GetFxImpactQuery(UserId), CancellationToken.None);

        result.Plans.Should().BeEmpty();
        result.BaseCurrency.Should().Be("USD");
        result.Totals.TotalSaving.Should().Be(0m);
    }

    [Theory]
    [InlineData("USD", "USD")]
    [InlineData("EUR", "EUR")]
    [InlineData("UAH", "UAH")]
    public async Task Handle_OnlyMajorOrBaseCurrencyPlans_ReturnsAnEmptyGate(string currency, string baseCurrency)
    {
        var handler = BuildHandler(baseCurrency, Plan(100m, currency, Today.AddDays(-5)));

        var result = await handler.Handle(new GetFxImpactQuery(UserId), CancellationToken.None);

        result.Plans.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AUahPlanForAnEurBasedUser_OpensTheGate()
    {
        var handler = BuildHandler("EUR", Plan(100m, "UAH", Today.AddDays(-5)));

        var result = await handler.Handle(new GetFxImpactQuery(UserId), CancellationToken.None);

        result.BaseCurrency.Should().Be("EUR");
        result.Plans.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_ASubscriptionInAForeignCurrency_OpensTheGate()
    {
        var handler = BuildHandler(
            "USD", Plan(500m, "UAH", Today.AddDays(-5), kind: SubscriptionKinds.Subscription));

        var result = await handler.Handle(new GetFxImpactQuery(UserId), CancellationToken.None);

        var plan = result.Plans.Should().ContainSingle().Subject;
        plan.Kind.Should().Be(SubscriptionKinds.Subscription);
        plan.PaymentsRemaining.Should().BeNull("an open-ended subscription has no remainder");
    }

    [Fact]
    public async Task Handle_ACurrencyNoFeedPublishes_IsLeftOutInsteadOfShownFlat()
    {
        var handler = BuildHandler("USD", Plan(100m, "XXX", Today.AddDays(-5)));

        var result = await handler.Handle(new GetFxImpactQuery(UserId), CancellationToken.None);

        result.Plans.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ABaseCurrencyNoFeedPublishes_ReturnsAnEmptyGate()
    {
        var handler = BuildHandler("XXX", Plan(100m, "UAH", Today.AddDays(-5)));

        var result = await handler.Handle(new GetFxImpactQuery(UserId), CancellationToken.None);

        result.Plans.Should().BeEmpty();
    }

    // ── the calculations ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_UsesUserSetStartDate_AsTheSigningDate()
    {
        // Plan signed May 2024; detection only saw the last few charges.
        var plan = Plan(14060.96m, "UAH", new DateOnly(2026, 8, 11), occurrenceCount: 3, startDate: Signing);

        var result = await BuildHandler("USD", plan).Handle(new GetFxImpactQuery(UserId), CancellationToken.None);

        var impact = result.Plans.Should().ContainSingle().Subject;
        impact.SigningDate.Should().Be(Signing);
        impact.SigningDateIsObserved.Should().BeFalse();
        impact.SigningUnitsPerBase.Should().BeApproximately(UahPerUsdAtSigning, 0.001m);
        impact.CurrentUnitsPerBase.Should().BeApproximately(UahPerUsdNow, 0.001m);
    }

    [Fact]
    public async Task Handle_WeakerLocalCurrency_MakesAFixedPaymentCheaper()
    {
        var plan = Plan(14060.96m, "UAH", new DateOnly(2026, 8, 11), startDate: Signing);

        var result = await BuildHandler("USD", plan).Handle(new GetFxImpactQuery(UserId), CancellationToken.None);

        var impact = result.Plans.Single();
        // 14060.96 / 39.5151 = 355.84 → 14060.96 / 44.5445 = 315.68
        impact.PaymentCostAtSigning.Should().BeApproximately(355.84m, 0.05m);
        impact.PaymentCostNow.Should().BeApproximately(315.68m, 0.05m);
        impact.CostChangePercent.Should().BeApproximately(-11.29m, 0.1m);
    }

    [Fact]
    public async Task Handle_WithoutStartDate_FallsBackToFirstObservedCharge_AndFlagsIt()
    {
        // 3 observed charges ending Aug 2026 → signing Jun 2026, after the rate move.
        var plan = Plan(14060.96m, "UAH", new DateOnly(2026, 8, 11), occurrenceCount: 3);

        var result = await BuildHandler("USD", plan).Handle(new GetFxImpactQuery(UserId), CancellationToken.None);

        var impact = result.Plans.Single();
        impact.SigningDate.Should().Be(new DateOnly(2026, 6, 11));
        impact.SigningDateIsObserved.Should().BeTrue();
        // Both ends sit after the rate move, so it understates rather than inventing a change.
        impact.CostChangePercent.Should().Be(0m);
        impact.PaidSaving.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_RemainingPayments_AreCostedAtTodaysRateAgainstTheSigningRate()
    {
        // 28 payments made (May 2024 → Aug 2026) of a 48-payment plan: 20 left.
        var plan = Plan(
            14060.96m, "UAH", new DateOnly(2026, 8, 11),
            occurrenceCount: 28, startDate: Signing, termCount: 48);

        var result = await BuildHandler("USD", plan).Handle(new GetFxImpactQuery(UserId), CancellationToken.None);

        var impact = result.Plans.Single();
        impact.PaymentsRemaining.Should().Be(20);
        impact.RemainingCostAtSigningRate.Should().BeApproximately(20 * 14060.96m / UahPerUsdAtSigning, 0.05m);
        impact.RemainingCostAtTodayRate.Should().BeApproximately(20 * 14060.96m / UahPerUsdNow, 0.05m);
        impact.RemainingSaving.Should().BeApproximately(
            (20 * 14060.96m / UahPerUsdAtSigning) - (20 * 14060.96m / UahPerUsdNow), 0.05m);
        impact.RemainingSaving.Should().BePositive("a weaker hryvnia makes the rest cheaper");
    }

    [Fact]
    public async Task Handle_PaymentsAlreadyMade_AreSavedAgainstTheSigningRateOnEachPaymentDate()
    {
        var plan = Plan(
            14060.96m, "UAH", new DateOnly(2026, 8, 11),
            occurrenceCount: 28, startDate: Signing, termCount: 48);

        var result = await BuildHandler("USD", plan).Handle(new GetFxImpactQuery(UserId), CancellationToken.None);

        var impact = result.Plans.Single();
        impact.PaymentsMade.Should().Be(28);
        // May 2024 → Dec 2025 paid at the signing rate (no saving); Jan → Aug 2026 (8 payments) at the new one.
        var perPayment = (14060.96m / UahPerUsdAtSigning) - (14060.96m / UahPerUsdNow);
        impact.PaidSaving.Should().BeApproximately(8 * perPayment, 0.05m);
    }

    [Fact]
    public async Task Handle_StrongerLocalCurrency_ReadsAsExtraCost()
    {
        var plan = Plan(
            1000m, "PLN", new DateOnly(2026, 8, 11),
            occurrenceCount: 28, startDate: Signing, termCount: 48);

        var result = await BuildHandler("USD", plan).Handle(new GetFxImpactQuery(UserId), CancellationToken.None);

        var impact = result.Plans.Single();
        impact.PaymentCostNow.Should().BeGreaterThan(impact.PaymentCostAtSigning);
        impact.CostChangePercent.Should().BeApproximately(12m, 0.01m);
        impact.PaidSaving.Should().BeApproximately(8 * 1000m * (UsdPerPlnAtSigning - UsdPerPlnNow), 0.01m);
        impact.RemainingSaving.Should().BeApproximately(20 * 1000m * (UsdPerPlnAtSigning - UsdPerPlnNow), 0.01m);
        result.Totals.TotalSaving.Should().BeNegative();
    }

    [Fact]
    public async Task Handle_ABaseCurrencyOtherThanUsd_ConvertsThroughTheCrossRate()
    {
        var plan = Plan(14060.96m, "UAH", new DateOnly(2026, 8, 11), startDate: Signing);

        var result = await BuildHandler("EUR", plan).Handle(new GetFxImpactQuery(UserId), CancellationToken.None);

        var impact = result.Plans.Single();
        // UAH per EUR = UAH per USD × USD per EUR.
        impact.SigningUnitsPerBase.Should().BeApproximately(UahPerUsdAtSigning * UsdPerEur, 0.01m);
        impact.CurrentUnitsPerBase.Should().BeApproximately(UahPerUsdNow * UsdPerEur, 0.01m);
        impact.PaymentCostNow.Should().BeApproximately(14060.96m / (UahPerUsdNow * UsdPerEur), 0.05m);
    }

    [Fact]
    public async Task Handle_PlanWithNoKnownRemainder_LeavesItOutOfTheRemainingSaving_AndSaysSo()
    {
        var known = Plan(
            14060.96m, "UAH", new DateOnly(2026, 8, 11),
            occurrenceCount: 28, startDate: Signing, termCount: 48);
        var open = Plan(500m, "UAH", new DateOnly(2026, 8, 5), kind: SubscriptionKinds.Subscription);

        var result = await BuildHandler("USD", known, open).Handle(new GetFxImpactQuery(UserId), CancellationToken.None);

        result.Plans.Should().HaveCount(2);
        result.Totals.HasUnknownRemainder.Should().BeTrue();
        result.Totals.RemainingSaving.Should().Be(
            result.Plans.Single(p => p.PaymentsRemaining is not null).RemainingSaving);
        result.Totals.TotalSaving.Should().BeApproximately(
            result.Totals.PaidSaving + result.Totals.RemainingSaving, 0.01m);
    }

    [Fact]
    public async Task Handle_SparklineRunsMonthlyFromSigningToToday()
    {
        var plan = Plan(14060.96m, "UAH", new DateOnly(2026, 8, 11), startDate: Signing);

        var result = await BuildHandler("USD", plan).Handle(new GetFxImpactQuery(UserId), CancellationToken.None);

        var rates = result.Plans.Single().Rates;
        rates[0].Date.Should().Be(Signing);
        rates[0].UnitsPerBase.Should().BeApproximately(UahPerUsdAtSigning, 0.001m);
        rates[^1].Date.Should().Be(Today);
        rates[^1].UnitsPerBase.Should().BeApproximately(UahPerUsdNow, 0.001m);
        rates.Select(r => r.Date).Should().BeInAscendingOrder();
    }
}
