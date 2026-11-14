namespace FinanceSentry.Modules.Research.Infrastructure.Sources.ReferenceUniverse;

using FinanceSentry.Modules.Research.Application.Services.ReferenceUniverse;
using FinanceSentry.Modules.Research.Domain.ReferenceUniverse;
using Microsoft.Extensions.Options;

/// <summary>
/// Theme lists declared as data under <c>ReferenceUniverse:Themes</c>. No network: the refresh just
/// materialises the configuration into the same table, so a theme member carries its lists like any other.
/// </summary>
public sealed class ThemeListFeed(IOptions<ReferenceUniverseOptions> options) : IReferenceListFeed
{
    public string Source => ReferenceSources.Themes;

    public Task<IReadOnlyList<ReferenceListSnapshot>> FetchAsync(CancellationToken ct = default)
        => Task.FromResult(Build(options.Value.Themes));

    public static IReadOnlyList<ReferenceListSnapshot> Build(Dictionary<string, List<string>> themes)
    {
        var snapshots = new List<ReferenceListSnapshot>();
        foreach (var (id, tickers) in themes.OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ReferenceFeedException("a theme is declared without an id");
            }

            var members = tickers
                .Select(ReferenceListKeys.NormalizeTicker)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .Select(t => new ReferenceMember(t, null))
                .ToList();
            if (members.Count == 0)
            {
                throw new ReferenceFeedException($"theme '{id}' declares no tickers");
            }

            snapshots.Add(new ReferenceListSnapshot(ReferenceListKeys.Theme(id), members));
        }

        return snapshots;
    }
}
