namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>Core-facing read of broken theses (017) over the internal thesis repository.</summary>
public sealed class BrokenThesisReader(IThesisRepository theses) : IBrokenThesisReader
{
    public async Task<IReadOnlyList<BrokenThesisSummary>> ListBrokenAsync(Guid userId, CancellationToken ct = default)
    {
        var all = await theses.ListUnscopedAsync(userId, ct);
        return all
            .Where(t => t.BrokenAt is not null)
            .Select(t => new BrokenThesisSummary(t.Id, t.Ticker.Trim().ToUpperInvariant(), t.BrokenAt!.Value))
            .ToList();
    }
}
