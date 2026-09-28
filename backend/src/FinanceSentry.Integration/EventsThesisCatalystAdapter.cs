namespace FinanceSentry.Integration;

using FinanceSentry.Modules.Events.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Feature 049 - <see cref="IThesisCatalystReader"/> over Research's published
/// <see cref="IActiveThesisCatalystReader"/> port (#673): the dated catalysts of the user's
/// unbroken theses. Catalysts are a jsonb list on the thesis, so the filter by date happens in the
/// query handler, not here.
/// </summary>
public sealed class EventsThesisCatalystAdapter(IActiveThesisCatalystReader catalysts) : IThesisCatalystReader
{
    public async Task<IReadOnlyList<ThesisCatalystEntry>> ListActiveAsync(Guid userId, CancellationToken ct = default)
    {
        var active = await catalysts.ListAsync(userId, ct);
        return active
            .Select(c => new ThesisCatalystEntry(c.ThesisId, c.Ticker, c.Date, c.Event))
            .ToList();
    }
}
