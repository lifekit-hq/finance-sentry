using System.Globalization;

namespace FinanceSentry.Modules.CryptoSync.Infrastructure.Binance;

/// <summary>
/// Binance's resume point for one asset: the next <c>fromId</c> per quote pair, written as
/// <c>USDT=1235,USDC=88</c>. Trade ids are per symbol, so one number cannot resume four pairs.
///
/// A bare number is the pre-#472 form (migrated from <c>CryptoHoldings.LastTradeId</c>) and applies
/// to every pair — the semantics the adapter had before the cursor was split per pair.
/// </summary>
public static class BinanceTradeCursor
{
    public static Dictionary<string, long> Parse(string? cursor, IEnumerable<string> quotes)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return result;
        }

        if (long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var legacy))
        {
            foreach (var quote in quotes)
            {
                result[quote] = legacy;
            }

            return result;
        }

        foreach (var part in cursor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            if (long.TryParse(part[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var fromId))
            {
                result[part[..separator]] = fromId;
            }
        }

        return result;
    }

    public static string? Format(IReadOnlyDictionary<string, long> nextFromIds)
    {
        var parts = nextFromIds
            .Where(kv => kv.Value > 0)
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => string.Create(CultureInfo.InvariantCulture, $"{kv.Key}={kv.Value}"))
            .ToList();

        return parts.Count == 0 ? null : string.Join(',', parts);
    }
}
