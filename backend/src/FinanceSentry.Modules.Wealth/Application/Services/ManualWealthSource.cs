namespace FinanceSentry.Modules.Wealth.Application.Services;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

/// <summary>
/// The values the person enters by hand (<see cref="Domain.ManualHolding"/>) as a <see cref="IWealthSource"/>, every
/// reading L3 (ESTIMATED). A cash-convertible entry is cash; any other is an "Other" position, so an in-economy value
/// (a Steam wallet) counts in total net worth and never in cash or the liquid figure built on it.
/// </summary>
public sealed class ManualWealthSource(IManualHoldingRepository holdings) : IWealthSource
{
    public string SourceId => WealthSourceIds.Manual;

    public EconomyKind Kind => EconomyKind.Manual;

    public async Task<WealthSourceReading> ReadAsync(Guid userId, CancellationToken ct = default)
    {
        var positions = new List<BookFigurePosition>();
        var cashUsd = 0m;
        var fxStatus = FxStatus.Exact;

        foreach (var h in await holdings.ListUnscopedAsync(userId, ct))
        {
            var usd = CurrencyConverter.Convert(h.Amount, h.Currency);
            fxStatus = fxStatus.Worst(usd.Status);
            if (h.Convertibility == Convertibility.Cash)
                cashUsd += usd.Amount;
            else
                positions.Add(new BookFigurePosition(h.Label, AssetClassNormalizer.Other, 1m, null, usd.Amount, SourceId));
        }

        return new WealthSourceReading(cashUsd, positions, fxStatus, Reliability.L3Estimated);
    }
}
