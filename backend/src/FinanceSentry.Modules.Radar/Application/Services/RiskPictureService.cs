using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Measures;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Ports;
using FinanceSentry.Modules.Radar.Domain.Repositories;

namespace FinanceSentry.Modules.Radar.Application.Services;

/// <summary>
/// The risk panel's figures from calculations that already exist: M8 (<see cref="RiskStatistics"/>) over the
/// brokerage book's cash-inclusive daily totals with the deposits and withdrawals taken out and link days bridged
/// (the series the book-vs-SPY return uses), the same M8 monthly returns over SPY's adjusted closes for beta
/// (<see cref="MarketBeta"/>), and the weights, position flag and drawdown tolerance the book and policy already
/// store. Nothing is stored and no figure is invented: a figure that cannot be measured carries its typed reason.
/// </summary>
public sealed class RiskPictureService(
    IDailyBarRepository bars,
    IPortfolioValueSource portfolioValues,
    IBrokerageFlowReader brokerageFlows,
    IRiskPictureInputsSource inputs) : IRiskPictureService
{
    private const string BenchmarkTicker = "SPY";

    private const int TopCount = 3;

    private const int HistoryYears = 10;

    public async Task<RiskPicture> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var earliest = today.AddYears(-HistoryYears);
        var snapshots = await portfolioValues.GetAsync(userId, earliest, today, ct);
        var flows = await brokerageFlows.ReadAsync(userId, earliest, ct);
        var policy = await inputs.ReadAsync(userId, ct);

        var bridge = ScopeLinks.Bridge(
            [.. snapshots.Where(s => s.BrokerageTotalUsd is not null).Select(s => new ValuePoint(s.Date, s.BrokerageTotalUsd))],
            flows.ClassifiedFlows,
            flows.Sources.Select(s => s.LinkedOn));
        var book = RiskStatistics.Measure(bridge.Values, bridge.Flows);

        var beta = MeasuredRate.Fails(MeasureFailure.NotEnoughHistory);
        var betaMonths = 0;
        if (book.MonthlyReturns.Count > 0)
        {
            var spyBars = await bars.GetSinceAsync(BenchmarkTicker, book.From.AddMonths(-1), ct);
            var spy = RiskStatistics.Measure([.. spyBars.Select(b => new ValuePoint(b.Date, b.AdjClose))], []);
            beta = MarketBeta.Measure(book.MonthlyReturns, spy.MonthlyReturns, out betaMonths);
        }

        var ranked = policy.Positions.OrderByDescending(p => p.Weight).ToList();
        var top3 = ranked.Take(TopCount).ToList();
        var top1 = ranked.FirstOrDefault();
        var crypto = policy.Positions.Where(p => p.AssetClass == AssetClassNormalizer.Crypto).Sum(p => p.Weight);

        return new RiskPicture(
            book.MonthlyReturns.Count > 0 ? book.From : null,
            book.MonthlyReturns.Count > 0 ? book.To : null,
            book.MonthlyReturns.Count,
            betaMonths,
            Figure(book.Volatility),
            Figure(book.MaxDrawdown),
            Figure(beta),
            policy.DrawdownTolerance,
            top1?.Symbol,
            top1?.Weight,
            [.. top3.Select(p => p.Symbol)],
            ranked.Count == 0 ? null : top3.Sum(p => p.Weight),
            ranked.Count == 0 ? null : crypto,
            policy.PositionFlag,
            policy.PositionFlag is { } flag && top1 is not null ? top1.Weight > flag : null);
    }

    private static RiskFigure Figure(MeasuredRate rate) => new(rate.Value, rate.Failure?.ToString());
}
