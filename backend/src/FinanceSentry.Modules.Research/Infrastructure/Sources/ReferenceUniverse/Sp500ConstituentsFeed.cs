namespace FinanceSentry.Modules.Research.Infrastructure.Sources.ReferenceUniverse;

using FinanceSentry.Modules.Research.Application.Services.ReferenceUniverse;
using FinanceSentry.Modules.Research.Domain.ReferenceUniverse;
using Microsoft.Extensions.Options;

/// <summary>
/// S&amp;P 500 members from datasets/s-and-p-500-companies (data licence ODC-PDDL-1.0, public domain; the
/// upstream compiles it from Wikipedia's list). The same upstream the embedded seed was cut from.
/// </summary>
public sealed class Sp500ConstituentsFeed(IHttpClientFactory http, IOptions<ReferenceUniverseOptions> options)
    : IReferenceListFeed
{
    public string Source => ReferenceSources.Sp500;

    public async Task<IReadOnlyList<ReferenceListSnapshot>> FetchAsync(CancellationToken ct = default)
    {
        var opts = options.Value;
        var rows = await ReferenceDatasetHttp.GetRowsAsync(http, opts.Sp500Url, ct);
        return [Parse(rows, opts.MinSp500Members)];
    }

    public static ReferenceListSnapshot Parse(IReadOnlyList<List<string>> rows, int minMembers)
    {
        var members = IndexCsv.ReadMembers(rows, "Symbol", "Security", minMembers, "S&P 500");
        return new ReferenceListSnapshot(ReferenceListKeys.Sp500, members);
    }
}
