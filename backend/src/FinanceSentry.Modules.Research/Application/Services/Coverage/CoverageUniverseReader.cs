namespace FinanceSentry.Modules.Research.Application.Services.Coverage;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>
/// Core-facing read of the coverage universe over the internal repository. Names with the freshest activity come
/// first, so a consumer that caps the list (the construction screen) keeps the names something is happening to.
/// </summary>
public sealed class CoverageUniverseReader(ICoverageRepository coverage) : ICoverageUniverseReader
{
    public async Task<IReadOnlyList<string>> ListTickersAsync(Guid userId, CancellationToken ct = default)
    {
        var members = await coverage.ListMembersUnscopedAsync(userId, ct);
        return members
            .OrderByDescending(m => m.LastEventAt ?? m.AddedAt)
            .ThenBy(m => m.Ticker, StringComparer.Ordinal)
            .Select(m => m.Ticker.Trim().ToUpperInvariant())
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();
    }
}
