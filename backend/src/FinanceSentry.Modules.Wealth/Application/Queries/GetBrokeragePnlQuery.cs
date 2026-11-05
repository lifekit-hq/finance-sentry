namespace FinanceSentry.Modules.Wealth.Application.Queries;

using System.Text.Json.Serialization;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;

/// <summary>How much of the sleeve a P&amp;L figure covers.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PnlStatus>))]
public enum PnlStatus
{
    /// <summary>Every position (or every sell in the window) is measured: the figure is the sleeve's.</summary>
    [JsonStringEnumMemberName("COMPLETE")]
    Complete,

    /// <summary>Some positions are measured, some are not: the figure covers part of the sleeve and says so.</summary>
    [JsonStringEnumMemberName("PARTIAL")]
    Partial,

    /// <summary>Nothing is measurable: no amount is stated, never a zero.</summary>
    [JsonStringEnumMemberName("UNAVAILABLE")]
    Unavailable,
}

/// <summary>
/// One side of the split. <see cref="Usd"/> is the measured amount (null when <see cref="Status"/> is UNAVAILABLE);
/// <see cref="PercentOfNetDeposited"/> is a percentage-points figure over the capital in the window, only for a COMPLETE
/// figure with a known denominator (a partial gain is never put over the whole book's capital).
/// <see cref="Measured"/> / <see cref="Total"/> count what is covered (positions for unrealized, symbols that sold
/// in the window for realized), <see cref="Coverage"/> is the measured share of value (unrealized only), and
/// <see cref="UnavailableSymbols"/> names what is left out and why the status is not COMPLETE.
/// </summary>
public sealed record PnlFigure(
    decimal? Usd,
    decimal? PercentOfNetDeposited,
    PnlStatus Status,
    int Measured,
    int Total,
    decimal? Coverage,
    IReadOnlyList<string> UnavailableSymbols);

/// <summary>
/// The brokerage sleeve's realized and unrealized P&amp;L on its capital in the window (issue 982), over the window
/// <see cref="From"/> to <see cref="To"/> of the sleeve's growth split. <see cref="NetDepositedUsd"/> is the capital
/// the percentages are put over: the window's opening market value, the value of providers linked inside it, and the
/// deposits less the withdrawals the brokers report. When the series opens after the account was funded, the opening
/// value already includes earlier market gains. It is null, with <see cref="SourcesWithoutFlows"/> naming why, while a
/// provider's deposits and withdrawals are unknown (they would read as return). Realized counts the sells dated after the
/// opening day; unrealized is the open positions' gain over their verified cost basis today (a point in time, so
/// <see cref="Realized"/> and <see cref="Unrealized"/> are two parts of a gain on cost, not a change in value over the
/// window). <see cref="Fx"/> is the worst conversion status of the fills behind the realized figure.
/// </summary>
public sealed record BrokeragePnlResponse(
    DateOnly? From,
    DateOnly? To,
    decimal? NetDepositedUsd,
    IReadOnlyList<string> SourcesWithoutFlows,
    PnlFigure Realized,
    PnlFigure Unrealized,
    FxStatus Fx = FxStatus.Exact,
    string Currency = "USD");

public record GetBrokeragePnlQuery(Guid UserId) : IQuery<BrokeragePnlResponse>;

public class GetBrokeragePnlQueryHandler(
    IQueryHandler<GetSleeveGrowthQuery, SleeveGrowthResponse> sleeveGrowth,
    IBrokeragePnlReader pnlReader)
    : IQueryHandler<GetBrokeragePnlQuery, BrokeragePnlResponse>
{
    private const int PercentDecimals = 2;
    private const decimal ToPercentPoints = 100m;

    public async Task<BrokeragePnlResponse> Handle(GetBrokeragePnlQuery query, CancellationToken cancellationToken)
    {
        var growth = await sleeveGrowth.Handle(
            new GetSleeveGrowthQuery(query.UserId, WealthSourceIds.Brokerage), cancellationToken);
        var pnl = await pnlReader.ReadAsync(query.UserId, growth.From, cancellationToken);

        var netDeposited = growth is { StartValue: { } start, NewlyLinked: { } linked, Contributed: { } contributed }
            ? start + linked + contributed
            : (decimal?)null;

        return new BrokeragePnlResponse(
            growth.From,
            growth.To,
            netDeposited,
            growth.SourcesWithoutFlows,
            Realized(pnl.Realized, netDeposited),
            Unrealized(pnl.Unrealized, netDeposited),
            pnl.Realized.Fx);
    }

    private static PnlFigure Realized(RealizedPnlReading realized, decimal? netDeposited)
    {
        var unavailable = realized.UnavailableSymbols.Select(s => s.Symbol).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var total = realized.SymbolsComputed + unavailable.Count;

        // No fills at all is not a zero; fills with no sell in the window are a real zero.
        if (!realized.HasFills || (realized.SymbolsComputed == 0 && unavailable.Count > 0))
        {
            return new PnlFigure(null, null, PnlStatus.Unavailable, 0, total, null, unavailable);
        }

        var status = unavailable.Count == 0 ? PnlStatus.Complete : PnlStatus.Partial;
        return new PnlFigure(
            realized.Usd,
            status == PnlStatus.Complete ? Percent(realized.Usd, netDeposited) : null,
            status,
            realized.SymbolsComputed,
            total,
            null,
            unavailable);
    }

    private static PnlFigure Unrealized(UnrealizedPnlReading unrealized, decimal? netDeposited)
    {
        if (unrealized.PositionsMeasured == 0)
        {
            return new PnlFigure(
                null, null, PnlStatus.Unavailable, 0, unrealized.PositionsTotal, null, unrealized.UnverifiedSymbols);
        }

        var complete = unrealized.PositionsMeasured == unrealized.PositionsTotal;
        decimal? coverage = unrealized.TotalValueUsd > 0m
            ? Math.Round(unrealized.MeasuredValueUsd / unrealized.TotalValueUsd, 4)
            : null;
        return new PnlFigure(
            unrealized.Usd,
            complete ? Percent(unrealized.Usd, netDeposited) : null,
            complete ? PnlStatus.Complete : PnlStatus.Partial,
            unrealized.PositionsMeasured,
            unrealized.PositionsTotal,
            coverage,
            unrealized.UnverifiedSymbols);
    }

    private static decimal? Percent(decimal usd, decimal? netDeposited)
        => netDeposited is > 0m ? Math.Round(usd / netDeposited.Value * ToPercentPoints, PercentDecimals) : null;
}
