namespace FinanceSentry.Modules.Alerts.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Alerts.API.Responses;
using FinanceSentry.Modules.Alerts.Domain.Repositories;

public record GetAlertsQuery(
    Guid UserId,
    string Filter,
    int Page,
    int PageSize) : IQuery<AlertsPageResponse>;

public class GetAlertsQueryHandler(IAlertRepository alerts) : IQueryHandler<GetAlertsQuery, AlertsPageResponse>
{
    private readonly IAlertRepository _alerts = alerts;

    public async Task<AlertsPageResponse> Handle(GetAlertsQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 100 ? 20 : request.PageSize;
        var filter = string.IsNullOrWhiteSpace(request.Filter) ? "all" : request.Filter;

        var (items, totalCount, unreadCount) = await _alerts.GetPagedAsync(
            request.UserId, filter, page, pageSize, cancellationToken);

        var dtos = items.Select(a => new AlertDto(
            a.Id, a.Type, a.Severity, a.Title, a.Message,
            a.ReferenceId, a.ReferenceLabel, a.IsRead, a.IsResolved,
            a.CreatedAt, a.ResolvedAt, a.OccurrenceCount, a.LastOccurredAt)).ToList();

        var totalPages = pageSize == 0 ? 0 : (int)Math.Ceiling((double)totalCount / pageSize);

        return new AlertsPageResponse(dtos, totalCount, unreadCount, page, pageSize, totalPages);
    }
}
