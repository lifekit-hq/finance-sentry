using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Ports;

namespace FinanceSentry.Modules.Radar.Application.Services;

/// <summary>
/// Income received by the book (dividends, interest, staking), a view over rows already collected: the brokerage
/// cash rows and crypto reward rows (<see cref="IBrokerageIncomeReader"/>, <see cref="ICryptoIncomeReader"/>), the
/// stored dividend yield and the ex-dividend calendar (<see cref="IIncomeOutlookSource"/>), and the held positions
/// (<see cref="IBrokerageHoldingsReader"/>). Withholding tax is netted and shown beside the gross. The forward
/// estimate is yield times value and the yield on cost uses only a verified cost basis, each null when its input is
/// missing; the parts are never combined into one score.
/// </summary>
public sealed class BookIncomeService(
    IBrokerageIncomeReader brokerageIncome,
    ICryptoIncomeReader cryptoIncome,
    IBrokerageHoldingsReader holdings,
    IIncomeOutlookSource outlook,
    TimeProvider clock) : IBookIncomeService
{
    private const string VerifiedBasis = "Verified";
    private const int ExDividendHorizonDays = 90;

    public async Task<BookIncome> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var ttmFrom = today.AddYears(-1);
        var ytdFrom = new DateOnly(today.Year, 1, 1).AddDays(-1);

        var receipts = new List<IncomeReceipt>();
        receipts.AddRange(await brokerageIncome.ReadAsync(userId, ttmFrom, ct));
        receipts.AddRange(await cryptoIncome.ReadAsync(userId, ttmFrom, ct));
        var valued = receipts.Where(r => r.Usd is not null && r.Date <= today).ToList();

        var positions = (await holdings.GetHoldingsAsync(userId, ct))
            .Where(h => h.Quantity != 0m && AssetClassNormalizer.Normalize(h.InstrumentType) != AssetClassNormalizer.Cash)
            .GroupBy(h => h.Symbol.Trim().ToUpperInvariant(), StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (
                    Value: g.Sum(h => h.UsdValue),
                    Cost: g.All(h => h.BasisState == VerifiedBasis && h.CostBasisUsd is not null)
                        ? g.Sum(h => h.CostBasisUsd!.Value)
                        : (decimal?)null),
                StringComparer.Ordinal);

        var outlooks = positions.Count == 0
            ? new Dictionary<string, IncomeOutlook>()
            : (await outlook.GetAsync([.. positions.Keys], today, today.AddDays(ExDividendHorizonDays), ct))
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

        var rows = new List<HoldingIncome>();
        var symbols = valued.Select(r => r.Symbol).Concat(positions.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        foreach (var symbol in symbols)
        {
            var own = valued.Where(r => r.Symbol == symbol).ToList();
            var ttm = Window(own, ttmFrom);
            var ytd = Window(own, ytdFrom);

            decimal? forward = null;
            decimal? yieldOnCost = null;
            DateOnly? exDate = null;
            if (positions.TryGetValue(symbol, out var position))
            {
                if (outlooks.TryGetValue(symbol, out var info))
                {
                    exDate = info.NextExDividend;
                    forward = info.DividendYield is { } y ? Math.Round(y * position.Value, 2) : null;
                }

                if (position.Cost is > 0m)
                    yieldOnCost = Math.Round(ttm.Net / position.Cost.Value, 4);
            }

            if (own.Count == 0 && forward is not > 0m)
                continue;

            rows.Add(new HoldingIncome(
                symbol, [.. own.Select(r => r.Kind).Distinct().Order()], ttm, ytd, forward, yieldOnCost, exDate));
        }

        var withCost = rows.Where(r => r.YieldOnCost is not null).Select(r => r.Symbol).ToList();
        var costTotal = withCost.Sum(s => positions[s].Cost!.Value);
        var withForward = rows.Where(r => r.ForwardAnnualUsd is not null).ToList();

        return new BookIncome(
            today,
            Window(valued, ttmFrom),
            Window(valued, ytdFrom),
            withForward.Count == 0 ? null : withForward.Sum(r => r.ForwardAnnualUsd!.Value),
            withCost.Count == 0 || costTotal <= 0m
                ? null
                : Math.Round(rows.Where(r => withCost.Contains(r.Symbol)).Sum(r => r.Ttm.Net) / costTotal, 4),
            receipts.Count(r => r.Usd is null),
            rows);
    }

    private static IncomeWindow Window(IEnumerable<IncomeReceipt> receipts, DateOnly after)
    {
        var inWindow = receipts.Where(r => r.Date > after).ToList();
        var gross = inWindow.Where(r => !r.IsWithholding).Sum(r => r.Usd!.Value);
        var withheld = -inWindow.Where(r => r.IsWithholding).Sum(r => r.Usd!.Value);
        return new IncomeWindow(Round(gross), Round(withheld), Round(gross - withheld));
    }

    private static decimal Round(decimal value) => Math.Round(value, 2);
}
