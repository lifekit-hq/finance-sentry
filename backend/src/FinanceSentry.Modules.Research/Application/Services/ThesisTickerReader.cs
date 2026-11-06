namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>Core-facing read of thesis tickers for the Radar universe, over the internal thesis repository.</summary>
public sealed class ThesisTickerReader(IThesisRepository theses) : IThesisTickerReader
{
    public async Task<IReadOnlyList<string>> ListTickersAsync(Guid userId, CancellationToken ct = default)
    {
        var all = await theses.ListUnscopedAsync(userId, ct);
        return all
            .Select(t => t.Ticker.Trim().ToUpperInvariant())
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();
    }
}
