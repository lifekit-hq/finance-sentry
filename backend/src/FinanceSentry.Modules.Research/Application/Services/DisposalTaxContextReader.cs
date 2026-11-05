namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Tax;

/// <summary>
/// Builds a position's <see cref="DisposalTaxContext"/> (#454 S6) from the reading the dossier already took of its
/// fills, the user's tax classification, and historical euro FX (one gap-filled series per currency, fetched only
/// when every gate before the lot walk can pass). Computed on read, nothing stored.
/// </summary>
public sealed class DisposalTaxContextReader(IInstrumentTaxRegimeSource regimes, IHistoricalExchangeRateService rates)
{
    private const string Usd = "USD";

    public async Task<DisposalTaxContext> ReadAsync(
        Guid userId, BookFigurePosition position, PositionReturnsReader.Reading reading, CancellationToken ct)
    {
        var regime = await regimes.GetForSymbolAsync(userId, position.Symbol, ct);
        var input = new DisposalTaxInput(
            position.Symbol,
            position.Provider,
            position.Quantity,
            position.UsdValue,
            reading.Returns.Coverage,
            position.BasisState,
            regime,
            reading.Ledger.Fills);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Probe the gates that need no FX; only a position that clears them pays for the rate series.
        var gated = DisposalTaxCalculator.Compute(input, today, (_, _) => 1m, _ => 1m);
        if (gated.Status == DisposalTaxCalculator.Withheld)
        {
            return gated;
        }

        var series = await FetchSeriesAsync(reading.Ledger, today, ct);
        return DisposalTaxCalculator.Compute(input, today, (c, d) => RateOn(series, c, d), RateNow);
    }

    private async Task<Dictionary<string, IReadOnlyDictionary<DateOnly, decimal>>> FetchSeriesAsync(
        PositionLedger ledger, DateOnly today, CancellationToken ct)
    {
        var currencies = ledger.Fills.Select(f => f.Currency)
            .Concat(ledger.Fills.Select(f => f.CommissionCurrency).OfType<string>())
            .Append(DisposalTaxCalculator.Eur)
            .Select(c => c.ToUpperInvariant())
            .Where(c => c != Usd)
            .Distinct()
            .ToList();

        var series = new Dictionary<string, IReadOnlyDictionary<DateOnly, decimal>>(StringComparer.OrdinalIgnoreCase);
        var from = ledger.Fills.Min(f => DateOnly.FromDateTime(f.TradedAt));
        foreach (var currency in currencies)
        {
            series[currency] = await rates.GetDailySeriesAsync(currency, from, today, ct);
        }

        return series;
    }

    private static decimal RateNow(string currency) => currency.Equals(Usd, StringComparison.OrdinalIgnoreCase)
        ? 1m
        : CurrencyConverter.ToUsd(1m, currency.ToUpperInvariant());

    private static decimal RateOn(
        Dictionary<string, IReadOnlyDictionary<DateOnly, decimal>> series, string currency, DateOnly day)
    {
        if (currency.Equals(Usd, StringComparison.OrdinalIgnoreCase))
        {
            return 1m;
        }

        return series.TryGetValue(currency, out var byDay) && byDay.TryGetValue(day, out var rate)
            ? rate
            : RateNow(currency);
    }
}
