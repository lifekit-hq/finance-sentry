namespace FinanceSentry.Integration;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Alerts.Application.Queries;
using FinanceSentry.Modules.Events.Domain.Ports;

/// <summary>Feature 049 - <see cref="IFiredAlertReader"/> over the Alerts module's <see cref="GetAlertsByTypesQuery"/>.</summary>
public sealed class EventsFiredAlertAdapter(
    IQueryHandler<GetAlertsByTypesQuery, AlertsByTypePage> alerts) : IFiredAlertReader
{
    public async Task<FiredAlertPage> ListAsync(
        Guid userId, IReadOnlyCollection<string> types, int page, int pageSize, CancellationToken ct = default)
    {
        var result = await alerts.Handle(new GetAlertsByTypesQuery(userId, types, page, pageSize), ct);
        var items = result.Items
            .Select(a => new FiredAlertRecord(
                a.Id, a.Type, a.Severity, a.Title, a.Message, a.ReferenceId, a.ReferenceLabel,
                a.IsRead, a.IsResolved, a.CreatedAt))
            .ToList();
        return new FiredAlertPage(items, result.TotalCount);
    }
}
