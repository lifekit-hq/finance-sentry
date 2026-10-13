namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Positions;

/// <summary>
/// Reads one holding's ingested fills and dividend rows and runs <see cref="PositionReturnCalculator"/> over
/// them with historical FX (one gap-filled series per currency, fetched up front). Shared by the dossier's
/// return decomposition and the chart's trade markers and average-cost line.
/// </summary>
public sealed class PositionReturnsReader(IPositionLedgerSource ledgerSource, IHistoricalExchangeRateService rates)
{
    private const string Usd = "USD";

    public sealed record Reading(PositionLedger Ledger, PositionReturns Returns);

    public async Task<Reading> ReadAsync(Guid userId, BookFigurePosition position, CancellationToken ct)
    {
        var ledger = await ledgerSource.ReadForSymbolAsync(userId, position.Symbol, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var dates = ledger.Fills.Select(f => DateOnly.FromDateTime(f.TradedAt))
            .Concat(ledger.Income.Select(i => DateOnly.FromDateTime(i.PaidAt)))
            .ToList();
        var currencies = ledger.Fills.Select(f => f.Currency)
            .Concat(ledger.Fills.Select(f => f.CommissionCurrency).OfType<string>())
            .Concat(ledger.Income.Select(i => i.Currency))
            .Select(c => c.ToUpperInvariant())
            .Where(c => c != Usd)
            .Distinct()
            .ToList();

        var series = new Dictionary<string, IReadOnlyDictionary<DateOnly, decimal>>(StringComparer.OrdinalIgnoreCase);
        if (dates.Count > 0)
        {
            var from = dates.Min();
            foreach (var currency in currencies)
            {
                series[currency] = await rates.GetDailySeriesAsync(currency, from, today, ct);
            }
        }

        decimal RateNow(string currency) => currency.Equals(Usd, StringComparison.OrdinalIgnoreCase)
            ? 1m
            : CurrencyConverter.ToUsd(1m, currency.ToUpperInvariant());

        decimal RateOn(string currency, DateOnly day)
        {
            if (currency.Equals(Usd, StringComparison.OrdinalIgnoreCase))
            {
                return 1m;
            }

            return series.TryGetValue(currency, out var byDay) && byDay.TryGetValue(day, out var rate)
                ? rate
                : RateNow(currency);
        }

        var returns = PositionReturnCalculator.Compute(
            ledger, position.Quantity, position.UsdValue, position.CostBasisUsd, RateOn, RateNow);
        return new Reading(ledger, returns);
    }
}
