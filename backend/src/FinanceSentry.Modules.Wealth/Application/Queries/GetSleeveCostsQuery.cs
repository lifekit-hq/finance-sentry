namespace FinanceSentry.Modules.Wealth.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;

/// <summary>
/// What one sleeve cost to run since the start of the calendar year, in USD until the controller applies the base
/// currency: trade <see cref="Commissions"/>, other <see cref="Fees"/> and transaction taxes, the cost of
/// <see cref="FxConversion"/> and the <see cref="Withholding"/> tax on income, with their <see cref="Total"/>.
/// <see cref="ShareOfReturns"/> is that total over the sleeve's growth before it (the year's
/// <see cref="SleeveGrowthResponse.Growth"/> with the costs added back, since growth is after them), a fraction. It is null
/// when the growth is unknown or not positive, never a zero. <see cref="UnvaluedRows"/> counts fee rows left out for want
/// of a USD value; <see cref="ApproximateRows"/> counts rows kept in the figures that were converted without a published
/// rate, with the typed reason codes in <see cref="FxReasons"/>. A view over rows already collected: the parts stay
/// separate and nothing is scored.
/// </summary>
public sealed record SleeveCostsResponse(
    DateOnly AsOf,
    DateOnly YearStart,
    decimal Commissions,
    decimal Fees,
    decimal FxConversion,
    decimal Withholding,
    decimal Total,
    decimal? ShareOfReturns,
    int UnvaluedRows,
    int ApproximateRows,
    IReadOnlyList<string> FxReasons,
    string Currency = "USD");

/// <summary>The cost of investing for <paramref name="Sleeve"/>: <see cref="WealthSourceIds.Brokerage"/> or <see cref="WealthSourceIds.Crypto"/>.</summary>
public record GetSleeveCostsQuery(Guid UserId, string Sleeve = WealthSourceIds.Brokerage) : IQuery<SleeveCostsResponse>;

public class GetSleeveCostsQueryHandler(
    IBrokerageCostReader brokerageCosts,
    ICryptoCostReader cryptoCosts,
    IQueryHandler<GetSleeveGrowthQuery, SleeveGrowthResponse> sleeveGrowth,
    TimeProvider clock)
    : IQueryHandler<GetSleeveCostsQuery, SleeveCostsResponse>
{
    private const int RateDecimals = 4;
    private const int MoneyDecimals = 2;

    public async Task<SleeveCostsResponse> Handle(GetSleeveCostsQuery query, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var yearStart = new DateOnly(today.Year, 1, 1);
        var dayBefore = yearStart.AddDays(-1);

        var reading = query.Sleeve switch
        {
            WealthSourceIds.Brokerage => await brokerageCosts.ReadAsync(query.UserId, dayBefore, cancellationToken),
            WealthSourceIds.Crypto => await cryptoCosts.ReadAsync(query.UserId, dayBefore, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(query), query.Sleeve, "No cost of investing for this sleeve."),
        };
        var items = reading.Items.Where(i => i.Date <= today).ToList();
        var approximate = items.Where(i => i.Fx.ToReason() is not null).ToList();
        var total = Sum(items, null);

        return new SleeveCostsResponse(
            today,
            yearStart,
            Sum(items, CostKind.Commission),
            Sum(items, CostKind.Fee),
            Sum(items, CostKind.FxConversion),
            Sum(items, CostKind.Withholding),
            total,
            await ShareOfReturnsAsync(query, items, total, dayBefore, cancellationToken),
            reading.UnvaluedRows,
            approximate.Count,
            [.. approximate.Select(i => i.Fx.ToReason()!).Distinct().Order(StringComparer.Ordinal)]);
    }

    // The year's growth is read over the same window as the costs, so a series that starts mid-year is not divided by
    // a full year of costs: only the costs after the window's opening close, up to its closing date, count.
    private async Task<decimal?> ShareOfReturnsAsync(
        GetSleeveCostsQuery query, List<CostItem> items, decimal total, DateOnly dayBefore, CancellationToken ct)
    {
        if (total <= 0m)
            return null;

        var growth = await sleeveGrowth.Handle(new GetSleeveGrowthQuery(query.UserId, query.Sleeve, dayBefore), ct);
        if (growth is not { Growth: { } net, From: { } opening, To: { } closing })
            return null;

        var windowCost = items.Where(i => i.Date > opening && i.Date <= closing).Sum(i => i.Usd);
        var gross = net + windowCost;
        return windowCost <= 0m || gross <= 0m ? null : Math.Round(windowCost / gross, RateDecimals);
    }

    private static decimal Sum(IEnumerable<CostItem> items, CostKind? kind)
        => Math.Round(items.Where(i => kind is null || i.Kind == kind).Sum(i => i.Usd), MoneyDecimals);
}
