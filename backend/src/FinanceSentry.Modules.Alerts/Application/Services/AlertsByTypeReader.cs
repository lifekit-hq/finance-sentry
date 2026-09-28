namespace FinanceSentry.Modules.Alerts.Application.Services;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Alerts.Application.Queries;
using FinanceSentry.Modules.Alerts.Domain.Ports;

/// <summary><see cref="IAlertsByTypeReader"/> impl over the Alerts module's own <see cref="GetAlertsByTypesQuery"/>.</summary>
public sealed class AlertsByTypeReader(
    IQueryHandler<GetAlertsByTypesQuery, AlertsByTypePage> alerts) : IAlertsByTypeReader
{
    public async Task<AlertsByTypeReadPage> ListAsync(
        Guid userId, IReadOnlyCollection<string> types, int page, int pageSize, CancellationToken ct = default)
    {
        var result = await alerts.Handle(new GetAlertsByTypesQuery(userId, types, page, pageSize), ct);
        var items = result.Items
            .Select(a => new AlertReadItem(
                a.Id, a.Type, a.Severity, a.Title, a.Message, a.ReferenceId, a.ReferenceLabel,
                a.IsRead, a.IsResolved, a.CreatedAt))
            .ToList();
        return new AlertsByTypeReadPage(items, result.TotalCount);
    }
}
