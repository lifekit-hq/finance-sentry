namespace FinanceSentry.Modules.Subscriptions.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Modules.Subscriptions.API.Responses;
using FinanceSentry.Modules.Subscriptions.Domain;
using FinanceSentry.Modules.Subscriptions.Domain.Repositories;

public record GetFxImpactQuery(Guid UserId) : IQuery<FxImpactResponse>;

/// <summary>
/// Prices plans billed in a currency other than USD, EUR or the user's base currency against the exchange
/// rate over time.
///
/// A UAH installment repaid out of euro income has a payment that never changes in hryvnia — what changes
/// is how much hard currency it takes to make that payment. Each plan is compared at its signing date
/// against today, and against the rate on every payment date already made, so the widget can say what the
/// rate has already saved and what it still saves on the payments left.
///
/// Only rates a feed actually published are used (never today's live rate standing in for history): a
/// plan whose currency has no published history is left out rather than shown with an invented flat line.
/// </summary>
public class GetFxImpactQueryHandler(
    IDetectedSubscriptionRepository repository,
    IHistoricalExchangeRateService rates,
    IUserBaseCurrencyReader baseCurrencyReader)
    : IQueryHandler<GetFxImpactQuery, FxImpactResponse>
{
    private const string Usd = "USD";
    private const string Eur = "EUR";
    private const int MonthsPerYear = 12;
    private const int RateDecimals = 4;
    private const int MoneyDecimals = 2;
    private const decimal PercentScale = 100m;

    public async Task<FxImpactResponse> Handle(GetFxImpactQuery request, CancellationToken cancellationToken)
    {
        var baseCurrency = (await baseCurrencyReader.GetAsync(request.UserId, cancellationToken))
            .Trim().ToUpperInvariant();

        var active = await repository.GetActiveByUserIdAsync(request.UserId.ToString(), cancellationToken);

        // Only a currency the user neither holds as base nor reports in (USD/EUR) is a story worth telling:
        // a plan already billed in the base currency, or one in the two majors, would show a flat line.
        var plans = active
            .Where(s => IsForeign(s.Currency, baseCurrency))
            .ToList();

        var empty = new FxImpactResponse(baseCurrency, [], new FxImpactTotals(0m, 0m, 0m, false));
        if (plans.Count == 0)
            return empty;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var earliest = plans.Min(SigningDateOf);

        var baseSeries = await UsdPerUnitSeriesAsync(baseCurrency, earliest, today, cancellationToken);
        if (baseSeries is null)
            return empty;

        var seriesByCurrency = new Dictionary<string, RateLeg>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var currency in plans.Select(p => p.Currency).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var series = await UsdPerUnitSeriesAsync(currency, earliest, today, cancellationToken);
            if (series is not null)
                seriesByCurrency[currency] = series;
        }

        var impacts = new List<FxImpactPlan>(plans.Count);
        foreach (var plan in plans)
        {
            if (!seriesByCurrency.TryGetValue(plan.Currency, out var planSeries))
                continue;

            var impact = Build(plan, planSeries, baseSeries, today);
            if (impact is not null)
                impacts.Add(impact);
        }

        return new FxImpactResponse(baseCurrency, impacts, BuildTotals(impacts));
    }

    /// <summary>
    /// The plan set the insight applies to: neither of the two majors nor the base currency. Public so the
    /// rule the widget's gate rests on is pinned by a test.
    /// </summary>
    public static bool IsForeign(string currency, string baseCurrency) =>
        !string.IsNullOrWhiteSpace(currency)
        && !string.Equals(currency, Usd, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(currency, Eur, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(currency, baseCurrency, StringComparison.OrdinalIgnoreCase);

    private static FxImpactPlan? Build(
        DetectedSubscription plan,
        RateLeg planSeries,
        RateLeg baseSeries,
        DateOnly today)
    {
        var signingDate = SigningDateOf(plan);

        var signingRate = BasePerUnitOn(planSeries, baseSeries, signingDate);
        var currentRate = BasePerUnitOn(planSeries, baseSeries, today);
        if (signingRate is not { } signing || currentRate is not { } current)
            return null;

        var paymentNative = plan.AverageAmount;
        var made = PaymentsMade(plan);
        var step = StepMonths(plan);

        // What the payments already made cost at their own dates' rates, against the signing rate. A date the
        // feed has no rate near is skipped rather than guessed, so a gap understates the saving, never invents one.
        var paidSaving = 0m;
        for (var i = 0; i < made; i++)
        {
            var paymentDate = signingDate.AddMonths(step * i);
            if (paymentDate > today)
                break;

            if (BasePerUnitOn(planSeries, baseSeries, paymentDate) is { } rateThen)
                paidSaving += paymentNative * (signing - rateThen);
        }

        var remaining = plan.RemainingPayments;
        decimal? remainingAtSigning = null, remainingAtToday = null, remainingSaving = null;
        if (remaining is int left)
        {
            remainingAtSigning = Money(paymentNative * left * signing);
            remainingAtToday = Money(paymentNative * left * current);
            remainingSaving = Money(paymentNative * left * (signing - current));
        }

        var costAtSigning = Money(paymentNative * signing);
        var costNow = Money(paymentNative * current);

        return new FxImpactPlan(
            plan.Id,
            plan.MerchantNameDisplay,
            plan.Kind,
            plan.Currency,
            paymentNative,
            signingDate,
            plan.StartDate is null,
            UnitsPerBase(signing),
            UnitsPerBase(current),
            costAtSigning,
            costNow,
            Percent(costAtSigning, costNow),
            made,
            remaining,
            remainingAtSigning,
            remainingAtToday,
            remainingSaving,
            Money(paidSaving),
            BuildSparkline(planSeries, baseSeries, signingDate, today));
    }

    private static FxImpactTotals BuildTotals(IReadOnlyList<FxImpactPlan> impacts)
    {
        var paid = Money(impacts.Sum(i => i.PaidSaving));
        var remaining = Money(impacts.Sum(i => i.RemainingSaving ?? 0m));
        return new FxImpactTotals(
            paid, remaining, Money(paid + remaining), impacts.Any(i => i.RemainingSaving is null));
    }

    /// <summary>Monthly quote from the signing date to today, ending on the latest published rate.</summary>
    private static List<FxRatePoint> BuildSparkline(
        RateLeg planSeries,
        RateLeg baseSeries,
        DateOnly from,
        DateOnly to)
    {
        var points = new List<FxRatePoint>();
        for (var date = from; date < to; date = date.AddMonths(1))
            AddPoint(points, planSeries, baseSeries, date);

        AddPoint(points, planSeries, baseSeries, to);
        return points;
    }

    private static void AddPoint(
        List<FxRatePoint> points,
        RateLeg planSeries,
        RateLeg baseSeries,
        DateOnly date)
    {
        if (BasePerUnitOn(planSeries, baseSeries, date) is { } rate)
            points.Add(new FxRatePoint(date, UnitsPerBase(rate)));
    }

    /// <summary>
    /// One currency's published USD-per-unit history over the window, or null when no feed published any.
    /// USD itself is the unit, so it needs no history.
    /// </summary>
    private async Task<RateLeg?> UsdPerUnitSeriesAsync(string currency, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (string.Equals(currency, Usd, StringComparison.OrdinalIgnoreCase))
            return new RateLeg(null);

        var series = await rates.GetPublishedSeriesAsync(currency, from, to, ct);
        return series.Count == 0 ? null : new RateLeg(series);
    }

    /// <summary>A currency's USD-per-unit quotes; <see cref="Published"/> is null for USD, which is always 1.</summary>
    private sealed record RateLeg(IReadOnlyDictionary<DateOnly, decimal>? Published)
    {
        public decimal? On(DateOnly date) =>
            Published is null
                ? 1m
                : PublishedRateLookup.Resolve(Published, date) is { } hit && hit.Rate > 0m ? hit.Rate : null;
    }

    /// <summary>Base-currency units per 1 unit of the plan's currency on a date, or null when either leg is unpublished.</summary>
    private static decimal? BasePerUnitOn(
        RateLeg planSeries,
        RateLeg baseSeries,
        DateOnly date) =>
        planSeries.On(date) is { } planUsd && baseSeries.On(date) is { } baseUsd
            ? planUsd / baseUsd
            : null;

    /// <summary>
    /// Where the comparison starts. A user-set start date wins; otherwise fall back to the oldest charge
    /// detection saw, which for a plan older than the lookback window is a conservative (understated)
    /// baseline rather than a wrong one.
    /// </summary>
    private static DateOnly SigningDateOf(DetectedSubscription plan) =>
        plan.StartDate ?? plan.LastChargeDate.AddMonths(-StepMonths(plan) * Math.Max(0, plan.OccurrenceCount - 1));

    private static int StepMonths(DetectedSubscription plan) =>
        plan.Cadence == SubscriptionCadences.Annual ? MonthsPerYear : 1;

    /// <summary>
    /// Payments made from the signing date. With a user-set start the months to the last charge count them
    /// (detection's lookback cannot); otherwise the charges detection counted.
    /// </summary>
    private static int PaymentsMade(DetectedSubscription plan)
    {
        if (plan.StartDate is not DateOnly start)
            return plan.OccurrenceCount;

        var months = ((plan.LastChargeDate.Year - start.Year) * MonthsPerYear) + plan.LastChargeDate.Month - start.Month;
        return Math.Max(plan.OccurrenceCount, (months / StepMonths(plan)) + 1);
    }

    /// <summary>Units of the plan's currency per 1 base unit — how a rate is normally quoted (UAH per EUR).</summary>
    private static decimal UnitsPerBase(decimal basePerUnit) =>
        basePerUnit > 0m ? Math.Round(1m / basePerUnit, RateDecimals) : 0m;

    private static decimal Percent(decimal from, decimal to) =>
        from == 0m ? 0m : Math.Round((to - from) / from * PercentScale, MoneyDecimals);

    private static decimal Money(decimal amount) => Math.Round(amount, MoneyDecimals);
}
