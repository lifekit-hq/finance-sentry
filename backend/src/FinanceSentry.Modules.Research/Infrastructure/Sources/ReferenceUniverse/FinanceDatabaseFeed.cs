namespace FinanceSentry.Modules.Research.Infrastructure.Sources.ReferenceUniverse;

using FinanceSentry.Modules.Research.Application.Services.ReferenceUniverse;
using FinanceSentry.Modules.Research.Domain.ReferenceUniverse;
using Microsoft.Extensions.Options;

/// <summary>
/// Sector and industry lists from FinanceDatabase's per-exchange equity CSVs (JerBouma/FinanceDatabase,
/// MIT; refreshed upstream every Sunday). Delisted rows and rows without a sector are dropped. Any one
/// exchange file failing fails the whole feed: a missing exchange would silently thin every list.
/// </summary>
public sealed class FinanceDatabaseFeed(IHttpClientFactory http, IOptions<ReferenceUniverseOptions> options)
    : IReferenceListFeed
{
    public string Source => ReferenceSources.FinanceDatabase;

    public async Task<IReadOnlyList<ReferenceListSnapshot>> FetchAsync(CancellationToken ct = default)
    {
        var opts = options.Value;
        if (opts.FinanceDatabaseExchanges.Count == 0)
        {
            throw new ReferenceFeedException("no FinanceDatabase exchange files are configured");
        }

        var files = new List<IReadOnlyList<List<string>>>();
        foreach (var exchange in opts.FinanceDatabaseExchanges)
        {
            var url = $"{opts.FinanceDatabaseBaseUrl.TrimEnd('/')}/{exchange}.csv";
            files.Add(await ReferenceDatasetHttp.GetRowsAsync(http, url, ct));
        }

        return Parse(files, opts.MinFinanceDatabaseMembers);
    }

    public static IReadOnlyList<ReferenceListSnapshot> Parse(
        IReadOnlyList<IReadOnlyList<List<string>>> files, int minMembers)
    {
        var lists = new Dictionary<string, Dictionary<string, ReferenceMember>>(StringComparer.Ordinal);
        var distinct = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rows in files)
        {
            if (rows.Count == 0)
            {
                throw new ReferenceFeedException("a FinanceDatabase exchange file is empty");
            }

            var map = CsvReader.MapHeader(rows[0], "symbol", "name", "sector", "industry", "delisted");
            foreach (var row in rows.Skip(1))
            {
                var ticker = ReferenceListKeys.NormalizeTicker(CsvReader.Cell(row, map, "symbol"));
                var sector = CsvReader.Cell(row, map, "sector");
                var delisted = CsvReader.Cell(row, map, "delisted");
                if (ticker is null || sector.Length == 0
                    || string.Equals(delisted, "true", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = CsvReader.Cell(row, map, "name");
                var member = new ReferenceMember(ticker, name.Length == 0 ? null : name);
                distinct.Add(ticker);
                Add(lists, ReferenceListKeys.Sector(sector), member);

                var industry = CsvReader.Cell(row, map, "industry");
                if (industry.Length > 0)
                {
                    Add(lists, ReferenceListKeys.Industry(industry), member);
                }
            }
        }

        if (distinct.Count < minMembers)
        {
            throw new ReferenceFeedException(
                $"FinanceDatabase payload has {distinct.Count} listed equities, fewer than the {minMembers} a real one holds");
        }

        return lists
            .OrderBy(l => l.Key, StringComparer.Ordinal)
            .Select(l => new ReferenceListSnapshot(l.Key, l.Value.Values.ToList()))
            .ToList();
    }

    private static void Add(Dictionary<string, Dictionary<string, ReferenceMember>> lists, string key, ReferenceMember m)
    {
        if (!lists.TryGetValue(key, out var members))
        {
            members = new Dictionary<string, ReferenceMember>(StringComparer.Ordinal);
            lists[key] = members;
        }

        members.TryAdd(m.Ticker, m);
    }
}
