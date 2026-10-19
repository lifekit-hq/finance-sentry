namespace FinanceSentry.Modules.Wealth.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Measures;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Ports;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

/// <summary>
/// M2 and M4 for one sleeve - brokerage (IBKR + Inzhur, idle broker cash included) or crypto (the venues, fiat held on
/// them included) - from the first close that holds the sleeve's value (<see cref="SeriesStart"/>; the daily value
/// series begins there) to the latest one, in USD until
/// the controller applies the base currency. <see cref="From"/> / <see cref="To"/> are the closes the measures
/// run between (a provider linked on the first close moves <see cref="From"/> past it).
/// <see cref="Contributed"/> is the deposits less the withdrawals the providers report; <see cref="NewlyLinked"/> the
/// value that entered with a newly connected provider (its link day's change, market moves of that day included);
/// <see cref="Growth"/> the rest of the change, split into <see cref="Income"/>, <see cref="Fees"/>,
/// <see cref="Taxes"/> and the market remainder. <see cref="MoneyWeightedReturn"/> is Modified Dietz over the
/// window (a rate, not annualised); <see cref="TimeWeightedReturn"/> is M3 over the same closes and flows, so the two
/// differ only by the timing of the flows (presentation pattern P8). It is null while the split is withheld or when
/// value appeared or vanished with no recorded flow (rule 4). <see cref="SourcesWithoutFlows"/> are providers whose deposits and withdrawals
/// are not known (a broker that has never reported one, a venue whose movements are not read or not yet walked): while any is listed the split cannot be made (its deposits would read as growth), so
/// the amounts are null and <see cref="Headline"/> is unavailable with <c>UNKNOWN_FLOW</c> naming each one. Value in
/// transit between two of the sleeve's providers counts in the sleeve's value on each day it is held by neither; a
/// transfer read as external only for want of a match marks the headline <c>UNMATCHED_TRANSFER</c>.
/// </summary>
public record SleeveGrowthResponse(
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
    string Currency = "USD",
    decimal? TimeWeightedReturn = null);

/// <summary>
/// The growth split for <paramref name="Sleeve"/>: <see cref="WealthSourceIds.Brokerage"/> or <see cref="WealthSourceIds.Crypto"/>.
/// <paramref name="From"/> / <paramref name="To"/> narrow the closes it runs over (the net-worth bridge's window);
/// left out, it runs over the whole series to today.
/// </summary>
public record GetSleeveGrowthQuery(
    Guid UserId, string Sleeve = WealthSourceIds.Brokerage, DateOnly? From = null, DateOnly? To = null)
    : IQuery<SleeveGrowthResponse>;

public class GetSleeveGrowthQueryHandler(
    IBrokerageValueHistoryReader history,
    IBrokerageFlowReader flowReader,
    INetWorthSnapshotRepository snapshots,
    ICryptoFlowReader cryptoFlowReader)
    : IQueryHandler<GetSleeveGrowthQuery, SleeveGrowthResponse>
{
    private const int RateDecimals = 6;

    public async Task<SleeveGrowthResponse> Handle(GetSleeveGrowthQuery query, CancellationToken cancellationToken)
    {
        var to = query.To ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var from = query.From ?? DateOnly.MinValue;
        var (held, flows) = query.Sleeve switch
        {
            WealthSourceIds.Brokerage => (await BrokerageDailyAsync(query.UserId, from, to, cancellationToken),
                await flowReader.ReadAsync(query.UserId, DateOnly.MinValue, cancellationToken)),
            WealthSourceIds.Crypto => (await CryptoDailyAsync(query.UserId, from, to, cancellationToken),
                await cryptoFlowReader.ReadAsync(query.UserId, DateOnly.MinValue, cancellationToken)),
            _ => throw new ArgumentOutOfRangeException(nameof(query), query.Sleeve, "No growth split for this sleeve."),
        };
        // Value moving between two of the sleeve's providers is held by neither, yet still the sleeve's.
        var daily = held.Select(d => d with { TotalUsd = d.TotalUsd + flows.InTransitOn(d.Date) }).ToList();
        var seriesStart = daily.FirstOrDefault(d => d.TotalUsd > 0m)?.Date;

        var bridge = ScopeLinks.Bridge(
            [.. daily.Select(d => new ValuePoint(d.Date, d.TotalUsd))],
            flows.ClassifiedFlows,
            flows.Sources.Select(s => s.LinkedOn));
        var scope = HeadlineScope.Sleeve(query.Sleeve);
        if (bridge.Opening is not { ValueUsd: { } start } opening
            || bridge.Closing is not { ValueUsd: { } end } closing
            || opening.Date >= closing.Date)
        {
            var missing = HeadlineFactory.Growth(
                null, MeasuredRate.Fails(MeasureFailure.InsufficientData), scope,
                seriesStart ?? to, to, HeadlineSignals.None);
            return new SleeveGrowthResponse(
                seriesStart, null, null, null, null, null, null, null, null, null, null, null,
                flows.SourcesWithoutFlows(to), missing);
        }

        var unknown = flows.SourcesWithoutFlows(closing.Date);
        var edges = daily.Where(d => d.Date == opening.Date || d.Date == closing.Date)
            .Select(d => d.Signals)
            .Aggregate(HeadlineSignals.None, (merged, s) => merged.Merge(s));
        var signals = edges with
        {
            NoSplit = false,
            Fx = edges.Fx.Worst(flows.FxWithin(opening.Date, closing.Date)),
            UnknownFlowSources = unknown,
            ScopeChanged = bridge.ScopeChanged,
            UnmatchedTransferSources = flows.UnmatchedTransferSources(opening.Date, closing.Date),
        };

        // The linked value is placed before the window's growth, so M2 neither counts it as a contribution nor as
        // growth; Modified Dietz weighs it as the external flow the bridge made of it.
        var measured = ContributionsAndGrowth.Measure(
            start + bridge.NewlyLinkedUsd, end, MeasureWindow.FlowsWithin(flows.ClassifiedFlows, opening.Date, closing.Date));
        var dietz = MoneyWeightedReturn.ModifiedDietz(opening, closing, bridge.Flows);
        var split = unknown.Count == 0 ? measured.Value : null;
        var twr = split is null || bridge.VanishedDays.Count > 0
            ? null
            : TimeWeightedReturn.Measure(bridge.Values, bridge.Flows);
        var headline = HeadlineFactory.Growth(
            measured.Value?.GrowthUsd, dietz, scope, opening.Date, closing.Date, signals);

        return new SleeveGrowthResponse(
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
            headline,
            TimeWeightedReturn: twr is { UnexplainedDays.Count: 0, Return.Value: { } twrRate }
                ? Math.Round(twrRate, RateDecimals)
                : null);
    }

    private async Task<IReadOnlyList<SleeveDay>> BrokerageDailyAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct)
        => [.. (await history.GetDailyAsync(userId, from, to, ct))
            .Select(d => new SleeveDay(d.Date, d.BrokerageTotalUsd, d.Signals ?? HeadlineSignals.None))];

    // Read like BrokerageValueHistoryReader: only the crypto source's own STALE_SOURCE reaches the sleeve's signals.
    private async Task<IReadOnlyList<SleeveDay>> CryptoDailyAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var raw = await snapshots.GetByUserIdUnscopedAsync(userId, from, to, ct);
        var withSources = SnapshotSources.OfEach(
            raw, await snapshots.GetSourcesUnscopedAsync(userId, [.. raw.Select(s => s.Id)], ct));
        return [.. withSources.Select(x => new SleeveDay(
            x.Snapshot.SnapshotDate,
            x.Snapshot.CryptoTotal,
            x.Sources.Signals(x.Snapshot) with
            {
                StaleSources = x.Sources.IsStale(WealthSourceIds.Crypto) ? [WealthSourceIds.Crypto] : [],
                EstimatedSources = [],
            }))];
    }

    private sealed record SleeveDay(DateOnly Date, decimal TotalUsd, HeadlineSignals Signals);
}
