namespace FinanceSentry.Modules.Research.Application.Services.Screens;

using FinanceSentry.Modules.Research.Application.Services.ReferenceUniverse;
using FinanceSentry.Modules.Research.Domain.ReferenceUniverse;
using Microsoft.Extensions.Options;

/// <summary>The tickers of the configured reference lists (stage 3 data), upper-case Yahoo form, ordered and distinct.</summary>
public sealed class ScreenUniverse(IReferenceUniverseReader reference, IOptions<ScreenOptions> options)
{
    public async Task<IReadOnlyList<string>> TickersAsync(CancellationToken ct = default)
    {
        var lists = await reference.GetListsAsync(ct);
        var wanted = new List<string>();
        foreach (var key in options.Value.ListKeys.Select(k => k.Trim()).Where(k => k.Length > 0))
        {
            if (key.EndsWith('*'))
            {
                var prefix = key[..^1];
                wanted.AddRange(lists.Where(l => l.ListKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(l => l.ListKey));
            }
            else
            {
                wanted.Add(key);
            }
        }

        var tickers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in wanted.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var member in await reference.GetMembersAsync(key, ct) ?? [])
            {
                if (ReferenceListKeys.NormalizeTicker(member.Ticker) is { } ticker)
                {
                    tickers.Add(ticker);
                }
            }
        }

        return [.. tickers.Order(StringComparer.Ordinal)];
    }
}
