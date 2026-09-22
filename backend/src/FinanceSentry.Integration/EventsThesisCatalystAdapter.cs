namespace FinanceSentry.Integration;

using FinanceSentry.Modules.Events.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>
/// Feature 049 - <see cref="IThesisCatalystReader"/> over Research's thesis repository: the dated
/// catalysts of the user's unbroken theses. Catalysts are a jsonb list on the thesis, so the filter
/// by date happens in the query handler, not here.
/// </summary>
public sealed class EventsThesisCatalystAdapter(IThesisRepository theses) : IThesisCatalystReader
{
    public async Task<IReadOnlyList<ThesisCatalystEntry>> ListActiveAsync(Guid userId, CancellationToken ct = default)
    {
        var all = await theses.ListAsync(userId, ct);
        return all
            .Where(t => t.BrokenAt is null)
            .SelectMany(t => t.Catalysts.Select(c => new ThesisCatalystEntry(t.Id, t.Ticker, c.Date, c.Event)))
            .ToList();
    }
}
