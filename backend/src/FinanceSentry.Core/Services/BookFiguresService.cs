namespace FinanceSentry.Core.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using Microsoft.Extensions.Logging;

/// <summary>
/// Single canonical implementation of <see cref="IBookFiguresService"/>.
/// Sums every registered <see cref="IWealthSource"/> once and returns a consistent book snapshot. Idle
/// brokerage cash and fiat held on crypto venues are already split from invested positions by their sources.
/// </summary>
public sealed class BookFiguresService(
    IEnumerable<IWealthSource> sources,
    ILogger<BookFiguresService> logger) : IBookFiguresService
{
    public async Task<BookFigures> ReadAsync(Guid userId, CancellationToken ct = default)
    {
        var positions = new List<BookFigurePosition>();
        var staleSources = new List<string>();
        var readings = new List<BookSourceReading>();
        var cashUsd = 0m;
        var bankingCashUsd = 0m;
        var brokerageCashUsd = 0m;
        var venueCashUsd = 0m;
        var fxStatus = FxStatus.Exact;

        foreach (var source in sources)
        {
            WealthSourceReading reading;
            try
            {
                reading = await source.ReadAsync(userId, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Wealth source {SourceId} unavailable for user {UserId}; contributing nothing.", source.SourceId, userId);
                staleSources.Add(source.SourceId);
                readings.Add(new BookSourceReading(source.SourceId, source.Kind, null));
                continue;
            }

            readings.Add(new BookSourceReading(source.SourceId, source.Kind, reading));
            positions.AddRange(reading.Positions);
            cashUsd += reading.CashUsd;
            fxStatus = fxStatus.Worst(reading.FxStatus);
            switch (source.Kind)
            {
                case EconomyKind.Bank:
                    bankingCashUsd += reading.CashUsd;
                    break;
                case EconomyKind.Brokerage:
                    brokerageCashUsd += reading.CashUsd;
                    break;
                case EconomyKind.CryptoVenue:
                    venueCashUsd += reading.CashUsd;
                    break;
            }
        }

        var investedValueUsd = positions.Sum(p => p.UsdValue);

        return new BookFigures(
            cashUsd,
            bankingCashUsd,
            brokerageCashUsd,
            investedValueUsd,
            investedValueUsd + cashUsd,
            positions,
            staleSources.Count > 0,
            staleSources,
            venueCashUsd,
            fxStatus)
        {
            Sources = readings,
        };
    }
}
