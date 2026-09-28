namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>
/// <see cref="IActiveThesisCatalystReader"/> impl over the internal thesis repository. Catalysts
/// are a jsonb list on the thesis, so any date filter belongs to the caller, not here.
/// </summary>
public sealed class ActiveThesisCatalystReader(IThesisRepository theses) : IActiveThesisCatalystReader
{
    public async Task<IReadOnlyList<ActiveThesisCatalyst>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var all = await theses.ListAsync(userId, ct);
        return all
            .Where(t => t.BrokenAt is null)
            .SelectMany(t => t.Catalysts.Select(c => new ActiveThesisCatalyst(t.Id, t.Ticker, c.Date, c.Event)))
            .ToList();
    }
}
