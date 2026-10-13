namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Recommendations;
using Microsoft.Extensions.Logging;

/// <summary>What a proposal on one ticker is sized against: the holding (if any), the book total and the price.</summary>
public sealed record RecommendationPosition(
    string Ticker,
    decimal Quantity,
    decimal PositionUsd,
    decimal BookTotalUsd,
    decimal UnitPriceUsd,
    decimal? LimitPrice,
    string? LimitCurrency,
    string Venue,
    bool Fractional,
    bool Held);

/// <summary>
/// Reads the book and a quote for a proposal (#1069). The book is the holdings source of truth; the quote, when one
/// comes back, gives the limit price in the listing's major currency and the USD unit price through
/// <see cref="CurrencyConverter"/>. Without a quote the unit price falls back to the holding's own USD value per unit.
/// Returns null when there is no price to size from. Job-safe: the book and quote reads carry no person.
/// </summary>
public sealed class RecommendationPositionReader(
    IBookFiguresService bookFigures,
    IMarketDataService marketData,
    ILogger<RecommendationPositionReader> logger)
{
    private const string DefaultVenue = "IBKR";

    public async Task<RecommendationPosition?> ReadAsync(Guid userId, string ticker, CancellationToken ct)
    {
        var book = await bookFigures.ReadAsync(userId, ct);
        return await ReadAsync(book, ticker, ct);
    }

    public async Task<RecommendationPosition?> ReadAsync(BookFigures book, string ticker, CancellationToken ct)
    {
        // A name held on more than one venue counts once for its weight; the order goes to the largest holding's venue.
        var matches = book.Positions
            .Where(p => p.Symbol.Equals(ticker, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.UsdValue)
            .ToList();
        var position = matches.FirstOrDefault();
        var quantity = matches.Sum(p => p.Quantity);
        var positionUsd = matches.Sum(p => p.UsdValue);
        var held = quantity > 0m;
        var fractional = position is not null
            && AssetClassNormalizer.Crypto.Equals(position.AssetClass, StringComparison.OrdinalIgnoreCase);

        var (limitPrice, currency) = fractional ? (null, null) : await TryQuoteAsync(ticker, ct);
        var unitUsd = limitPrice is decimal lp
            ? CurrencyConverter.ToUsd(lp, currency ?? "USD")
            : held ? positionUsd / quantity : 0m;

        if (unitUsd <= 0m)
        {
            return null;
        }

        if (limitPrice is null && held)
        {
            limitPrice = PriceRounding.Round(unitUsd);
            currency = "USD";
        }

        return new RecommendationPosition(
            ticker,
            held ? quantity : 0m,
            held ? positionUsd : 0m,
            book.TotalValueUsd,
            unitUsd,
            limitPrice,
            currency,
            position is null ? DefaultVenue : RecommendationOrderText.VenueLabel(position.Provider),
            fractional,
            held);
    }

    private async Task<(decimal? Price, string? Currency)> TryQuoteAsync(string ticker, CancellationToken ct)
    {
        try
        {
            var quotes = await marketData.GetQuotesAsync([ticker], ct);
            if (quotes.TryGetValue(ticker, out var quote) && quote.Price > 0m)
            {
                var (currency, divisor) = MinorUnitCurrency.Normalize(quote.Currency);
                return (quote.Price / divisor, currency ?? "USD");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Recommendation sizing: quote unavailable for {Ticker}; using the book price", ticker);
        }

        return (null, null);
    }
}
