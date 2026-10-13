namespace FinanceSentry.Modules.Research.Domain.Recommendations;

using System.Globalization;

/// <summary>The figures a proposal is sized from, all in USD except the quantity.</summary>
public sealed record RecommendationSizingInput(
    RecommendationAction Action,
    decimal PositionQuantity,
    decimal PositionUsd,
    decimal BookTotalUsd,
    decimal UnitPriceUsd,
    bool Fractional,
    decimal? RequestedUsd,
    decimal? CapFraction);

public sealed record RecommendationSize(
    decimal SizeUsd,
    decimal SizeShares,
    decimal ShareOfBookBeforePct,
    decimal ShareOfBookAfterPct);

/// <summary>
/// Pure sizing for a proposal (#1069). Exit sells the whole position; Trim sells the requested amount or, with none,
/// what takes the position back to the single-position cap; Add buys the requested amount. Whole shares for listed
/// names - a trim rounds up so it reaches the cap, an add rounds down so it stays within it - and six decimals for a
/// fractional (crypto) holding. Cash stays in the book either way, so the book total is the denominator before and after.
/// </summary>
public static class RecommendationSizing
{
    private const int FractionalDecimals = 6;
    private const int PercentDecimals = 2;
    private const decimal Percent = 100m;

    public static RecommendationSize? Size(RecommendationSizingInput input)
    {
        if (input.BookTotalUsd <= 0m || input.UnitPriceUsd <= 0m)
        {
            return null;
        }

        var shares = input.Action switch
        {
            RecommendationAction.Exit => input.PositionQuantity,
            RecommendationAction.Trim => TrimShares(input),
            RecommendationAction.Add => AddShares(input),
            _ => (decimal?)null,
        };

        if (shares is not decimal s || s <= 0m)
        {
            return null;
        }

        var usd = input.Action == RecommendationAction.Exit
            ? input.PositionUsd
            : Math.Round(s * input.UnitPriceUsd, 2);
        var after = input.Action == RecommendationAction.Add
            ? input.PositionUsd + usd
            : Math.Max(0m, input.PositionUsd - usd);

        return new RecommendationSize(
            usd,
            s,
            Pct(input.PositionUsd, input.BookTotalUsd),
            Pct(after, input.BookTotalUsd));
    }

    /// <summary>The USD a trim takes off to bring the position back to the cap; null when it is within the cap.</summary>
    public static decimal? ExcessOverCapUsd(decimal positionUsd, decimal bookTotalUsd, decimal? capFraction)
    {
        if (capFraction is not decimal cap || bookTotalUsd <= 0m)
        {
            return null;
        }

        var excess = positionUsd - (cap * bookTotalUsd);
        return excess > 0m ? excess : null;
    }

    private static decimal? TrimShares(RecommendationSizingInput input)
    {
        var usd = input.RequestedUsd ?? ExcessOverCapUsd(input.PositionUsd, input.BookTotalUsd, input.CapFraction);
        if (usd is not decimal u || u <= 0m)
        {
            return null;
        }

        var raw = u / input.UnitPriceUsd;
        var shares = input.Fractional
            ? Math.Round(raw, FractionalDecimals, MidpointRounding.ToPositiveInfinity)
            : Math.Ceiling(raw);
        return Math.Min(shares, input.PositionQuantity);
    }

    private static decimal? AddShares(RecommendationSizingInput input)
    {
        if (input.RequestedUsd is not decimal u || u <= 0m)
        {
            return null;
        }

        var raw = u / input.UnitPriceUsd;
        return input.Fractional
            ? Math.Round(raw, FractionalDecimals, MidpointRounding.ToZero)
            : Math.Floor(raw);
    }

    private static decimal Pct(decimal part, decimal total) => Math.Round(part / total * Percent, PercentDecimals);
}

/// <summary>
/// The order line the owner would type at the broker, e.g. <c>SELL 660 GRAB / LIMIT 3.13 USD DAY / IBKR</c>. Text only:
/// it is shown and copied, never sent anywhere.
/// </summary>
public static class RecommendationOrderText
{
    private const decimal WholeUnitPrice = 1m;

    public static string? Build(
        RecommendationAction action, string ticker, decimal? shares, decimal? limitPrice, string? currency, string? venue)
    {
        if (shares is not decimal s || s <= 0m || action == RecommendationAction.Hold)
        {
            return null;
        }

        var side = action == RecommendationAction.Add ? "BUY" : "SELL";
        var parts = new List<string> { $"{side} {FormatQuantity(s)} {ticker}" };
        parts.Add(limitPrice is decimal p && p > 0m
            ? $"LIMIT {FormatPrice(p)} {currency ?? "USD"} DAY"
            : "LIMIT DAY");
        if (!string.IsNullOrWhiteSpace(venue))
        {
            parts.Add(venue);
        }

        return string.Join(" / ", parts);
    }

    /// <summary>The broker a holding's provider names, as the owner knows it - never an account number.</summary>
    public static string VenueLabel(string provider)
        => provider.Trim().ToLowerInvariant() switch
        {
            "ibkr" => "IBKR",
            "binance" => "Binance",
            "revolutx" or "revolut-x" or "revolut_x" => "Revolut X",
            "" => "your broker",
            var other => char.ToUpperInvariant(other[0]) + other[1..],
        };

    public static string FormatQuantity(decimal quantity)
        => quantity.ToString(quantity == Math.Truncate(quantity) ? "0" : "0.######", CultureInfo.InvariantCulture);

    private static string FormatPrice(decimal price)
        => price >= WholeUnitPrice
            ? Math.Round(price, 2).ToString("0.00", CultureInfo.InvariantCulture)
            : PriceRounding.Round(price).ToString("0.############", CultureInfo.InvariantCulture);
}
