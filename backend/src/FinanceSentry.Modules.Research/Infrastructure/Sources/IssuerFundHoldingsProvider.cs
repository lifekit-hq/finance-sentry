namespace FinanceSentry.Modules.Research.Infrastructure.Sources;

using System.Globalization;
using System.Text;
using FinanceSentry.Modules.Research.Application.Services.Etf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// The fallback for a fund Yahoo lists no holdings for (issue #1324): the issuer's own holdings file, a CSV the
/// operator maps per fund in <see cref="FundHoldingsOptions.IssuerFiles"/>. Issuers differ in preamble and column
/// names, so <see cref="Parse"/> finds the header row by its ticker and weight columns instead of by position. A fund
/// with no mapped file, a failed download or a file with no readable rows answers a <see cref="FundKind.Fund"/> with no
/// holdings (this provider is only asked about symbols the quote path already flagged as not an equity).
/// </summary>
public sealed class IssuerFundHoldingsProvider(
    IHttpClientFactory httpFactory,
    IOptions<FundHoldingsOptions> options,
    TimeProvider clock,
    ILogger<IssuerFundHoldingsProvider> logger) : IFundHoldingsProvider
{
    public const string HttpClientName = "fund-holdings-issuer";

    private const int HeaderSearchLines = 60;
    private const decimal FractionTotalCeiling = 1.5m;
    private const decimal PercentScale = 100m;
    private const int WeightDecimals = 2;

    private static readonly string[] TickerColumns = ["ticker", "symbol", "issuer ticker", "holding ticker"];

    private static readonly string[] WeightColumns =
    [
        "weight (%)", "weight", "% weight", "% of net assets", "% of funds", "% of fund", "percent of fund",
        "market value weight", "weighting",
    ];

    private static readonly string[] NameColumns = ["name", "holding", "holding name", "security name", "description"];

    public async Task<FundHoldingsLookup> LookupAsync(string symbol, CancellationToken ct)
    {
        var upper = symbol.Trim().ToUpperInvariant();
        var empty = new FundHoldingsLookup(FundKind.Fund, [], null, null);
        if (!options.Value.IssuerFiles.TryGetValue(upper, out var url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            return empty;
        }

        try
        {
            var client = httpFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(uri, ct);
            response.EnsureSuccessStatusCode();
            var holdings = Parse(await response.Content.ReadAsStringAsync(ct), FundHoldingsLookup.TopCount);
            return holdings.Count == 0
                ? empty
                : new FundHoldingsLookup(FundKind.Fund, holdings, FundHoldingsSources.Issuer, clock.GetUtcNow());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Issuer holdings file for {Symbol} could not be read", upper);
            return empty;
        }
    }

    /// <summary>
    /// The largest <paramref name="take"/> holdings of an issuer CSV, weights in percent. The header is the first row
    /// (within the first lines) that has both a ticker and a weight column; rows without a readable positive weight
    /// are dropped, and a file whose weights total no more than 1.5 is read as fractions. Empty when no header is found.
    /// </summary>
    public static IReadOnlyList<FundHolding> Parse(string csv, int take)
    {
        var lines = csv.TrimStart('﻿').Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < Math.Min(lines.Length, HeaderSearchLines); i++)
        {
            var header = SplitCsv(lines[i]).Select(c => c.Trim().ToLowerInvariant()).ToList();
            var ticker = header.FindIndex(c => TickerColumns.Contains(c));
            var weight = header.FindIndex(c => WeightColumns.Contains(c));
            if (ticker < 0 || weight < 0)
            {
                continue;
            }

            var name = header.FindIndex(c => NameColumns.Contains(c));
            return ReadRows(lines.Skip(i + 1), ticker, weight, name, take);
        }

        return [];
    }

    private static List<FundHolding> ReadRows(IEnumerable<string> rows, int ticker, int weight, int name, int take)
    {
        var parsed = new List<FundHolding>();
        foreach (var row in rows)
        {
            var cells = SplitCsv(row);
            if (cells.Count <= Math.Max(ticker, weight) || !TryWeight(cells[weight], out var w) || w <= 0m)
            {
                continue;
            }

            var symbol = cells[ticker].Trim();
            var label = name >= 0 && cells.Count > name ? cells[name].Trim() : null;
            parsed.Add(new FundHolding(
                symbol.Length == 0 || symbol == "-" ? null : symbol.ToUpperInvariant(),
                string.IsNullOrEmpty(label) ? null : label,
                w));
        }

        var scale = parsed.Sum(h => h.WeightPct) <= FractionTotalCeiling ? PercentScale : 1m;
        return parsed
            .Select(h => h with { WeightPct = decimal.Round(h.WeightPct * scale, WeightDecimals) })
            .OrderByDescending(h => h.WeightPct)
            .Take(take)
            .ToList();
    }

    private static bool TryWeight(string cell, out decimal weight) =>
        decimal.TryParse(
            cell.Replace("%", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal).Trim(),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out weight);

    /// <summary>One CSV line into cells: commas split, double quotes group, a doubled quote is a quote.</summary>
    private static List<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted && c == '"' && i + 1 < line.Length && line[i + 1] == '"')
            {
                cell.Append('"');
                i++;
            }
            else if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == ',' && !quoted)
            {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else if (c != '\r')
            {
                cell.Append(c);
            }
        }

        cells.Add(cell.ToString());
        return cells;
    }
}
