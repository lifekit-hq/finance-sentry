namespace FinanceSentry.Modules.Wealth.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Measures;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Wealth.Domain.Ports;

/// <summary>
/// M2 and M4 for the brokerage sleeve (IBKR + Inzhur, idle broker cash included) from the first close that holds
/// brokerage value (<see cref="SeriesStart"/>; the daily value series begins there) to the latest one, in USD until
/// the controller applies the base currency. <see cref="From"/> / <see cref="To"/> are the closes the measures
/// run between (a provider linked on the first close moves <see cref="From"/> past it).
/// <see cref="Contributed"/> is the deposits less the withdrawals the brokers report; <see cref="NewlyLinked"/> the
/// value that entered with a newly connected provider (its link day's change, market moves of that day included);
/// <see cref="Growth"/> the rest of the change, split into <see cref="Income"/>, <see cref="Fees"/>,
/// <see cref="Taxes"/> and the market remainder. <see cref="MoneyWeightedReturn"/> is Modified Dietz over the
/// window (a rate, not annualised). <see cref="SourcesWithoutFlows"/> are providers that have never reported a
/// deposit or withdrawal: while any is listed the split cannot be made (its deposits would read as growth), so
/// the amounts are null and <see cref="Headline"/> is unavailable with <c>UNKNOWN_FLOW</c> naming each one.
/// </summary>
public record BrokerageGrowthResponse(
    DateOnly? SeriesStart,
    DateOnly? From,
    DateOnly? To,
    decimal? StartValue,
    decimal? EndValue,
    decimal? Contributed,
    decimal? NewlyLinked,
    decimal? Growth,
    decimal? Income,
    decimal? Fees,
    decimal? Taxes,
    decimal? MoneyWeightedReturn,
    IReadOnlyList<string> SourcesWithoutFlows,
    Headline Headline,
    string Currency = "USD");

public record GetBrokerageGrowthQuery(Guid UserId) : IQuery<BrokerageGrowthResponse>;

public class GetBrokerageGrowthQueryHandler(
    IBrokerageValueHistoryReader history,
    IBrokerageFlowReader flowReader)
    : IQueryHandler<GetBrokerageGrowthQuery, BrokerageGrowthResponse>
{
    private const int RateDecimals = 6;

    public async Task<BrokerageGrowthResponse> Handle(GetBrokerageGrowthQuery query, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var daily = await history.GetDailyAsync(query.UserId, DateOnly.MinValue, today, cancellationToken);
        var flows = await flowReader.ReadAsync(query.UserId, DateOnly.MinValue, cancellationToken);
        var seriesStart = daily.FirstOrDefault(d => d.BrokerageTotalUsd > 0m)?.Date;

        var bridge = ScopeLinks.Bridge(
            [.. daily.Select(d => new ValuePoint(d.Date, d.BrokerageTotalUsd))],
            flows.ClassifiedFlows,
            flows.Sources.Select(s => s.LinkedOn));
        var scope = HeadlineScope.Sleeve(WealthSourceIds.Brokerage);
        if (bridge.Opening is not { ValueUsd: { } start } opening
            || bridge.Closing is not { ValueUsd: { } end } closing
            || opening.Date >= closing.Date)
        {
            var missing = HeadlineFactory.Growth(
                null, MeasuredRate.Fails(MeasureFailure.InsufficientData), scope,
                seriesStart ?? today, today, HeadlineSignals.None);
            return new BrokerageGrowthResponse(
                seriesStart, null, null, null, null, null, null, null, null, null, null, null,
                flows.SourcesWithoutFlows(today), missing);
        }

        var unknown = flows.SourcesWithoutFlows(closing.Date);
        var edges = daily.Where(d => d.Date == opening.Date || d.Date == closing.Date)
            .Select(d => d.Signals ?? HeadlineSignals.None)
            .Aggregate(HeadlineSignals.None, (merged, s) => merged.Merge(s));
        var signals = edges with
        {
            NoSplit = false,
            Fx = edges.Fx.Worst(flows.FxWithin(opening.Date, closing.Date)),
            UnknownFlowSources = unknown,
            ScopeChanged = bridge.ScopeChanged,
        };

        // The linked value is placed before the window's growth, so M2 neither counts it as a contribution nor as
        // growth; Modified Dietz weighs it as the external flow the bridge made of it.
        var measured = ContributionsAndGrowth.Measure(
            start + bridge.NewlyLinkedUsd, end, MeasureWindow.FlowsWithin(flows.ClassifiedFlows, opening.Date, closing.Date));
        var dietz = MoneyWeightedReturn.ModifiedDietz(opening, closing, bridge.Flows);
        var split = unknown.Count == 0 ? measured.Value : null;
        var headline = HeadlineFactory.Growth(
            measured.Value?.GrowthUsd, dietz, scope, opening.Date, closing.Date, signals);

        return new BrokerageGrowthResponse(
            seriesStart,
            opening.Date,
            closing.Date,
            start,
            end,
            split?.ContributionsUsd,
            split is null ? null : bridge.NewlyLinkedUsd,
            split?.GrowthUsd,
            split?.IncomeUsd,
            split?.FeesUsd,
            split?.TaxesUsd,
            split is null || dietz.Value is not { } rate ? null : Math.Round(rate, RateDecimals),
            unknown,
            headline);
    }
}
