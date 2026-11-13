namespace FinanceSentry.Modules.Research.Infrastructure.Sources.ReferenceUniverse;

using FinanceSentry.Modules.Research.Application.Services.ReferenceUniverse;
using FinanceSentry.Modules.Research.Domain.ReferenceUniverse;
using Microsoft.Extensions.Options;

/// <summary>Current Nasdaq-100 members from unliftedq/index-constitution's <c>latest/nasdaq100.csv</c> (MIT).</summary>
public sealed class Nasdaq100Feed(IHttpClientFactory http, IOptions<ReferenceUniverseOptions> options)
    : IReferenceListFeed
{
    public string Source => ReferenceSources.Nasdaq100;

    public async Task<IReadOnlyList<ReferenceListSnapshot>> FetchAsync(CancellationToken ct = default)
    {
        var opts = options.Value;
        var rows = await ReferenceDatasetHttp.GetRowsAsync(http, opts.Nasdaq100Url, ct);
        return [Parse(rows, opts.MinNasdaq100Members)];
    }

    public static ReferenceListSnapshot Parse(IReadOnlyList<List<string>> rows, int minMembers)
    {
        var members = IndexCsv.ReadMembers(rows, "symbol", "name", minMembers, "Nasdaq-100");
        return new ReferenceListSnapshot(ReferenceListKeys.Nasdaq100, members);
    }
}

/// <summary>Reads a two-column (ticker, name) index CSV into distinct normalised members.</summary>
internal static class IndexCsv
{
    public static List<ReferenceMember> ReadMembers(
        IReadOnlyList<List<string>> rows, string tickerColumn, string nameColumn, int minMembers, string label)
    {
        if (rows.Count == 0)
        {
            throw new ReferenceFeedException($"{label} payload is empty");
        }

        var map = CsvReader.MapHeader(rows[0], tickerColumn, nameColumn);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var members = new List<ReferenceMember>();
        foreach (var row in rows.Skip(1))
        {
            var ticker = ReferenceListKeys.NormalizeTicker(CsvReader.Cell(row, map, tickerColumn));
            if (ticker is not null && seen.Add(ticker))
            {
                var name = CsvReader.Cell(row, map, nameColumn);
                members.Add(new ReferenceMember(ticker, name.Length == 0 ? null : name));
            }
        }

        if (members.Count < minMembers)
        {
            throw new ReferenceFeedException(
                $"{label} payload has {members.Count} members, fewer than the {minMembers} a real list holds");
        }

        return members;
    }
}
