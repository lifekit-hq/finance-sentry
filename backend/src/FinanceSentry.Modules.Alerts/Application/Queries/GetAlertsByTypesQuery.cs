namespace FinanceSentry.Modules.Alerts.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Alerts.API.Responses;
using FinanceSentry.Modules.Alerts.Domain.Repositories;

/// <summary>
/// Non-dismissed alerts of the given types, newest first, paged (feature 049). A read the Events
/// module consumes through its <c>IFiredAlertReader</c> port; nothing on the write side changes.
/// </summary>
public record GetAlertsByTypesQuery(
    Guid UserId,
    IReadOnlyCollection<string> Types,
    int Page,
    int PageSize) : IQuery<AlertsByTypePage>;

public record AlertsByTypePage(IReadOnlyList<AlertDto> Items, int TotalCount);

public class GetAlertsByTypesQueryHandler(IAlertRepository alerts) : IQueryHandler<GetAlertsByTypesQuery, AlertsByTypePage>
{
    public async Task<AlertsByTypePage> Handle(GetAlertsByTypesQuery request, CancellationToken cancellationToken)
    {
        if (request.Types.Count == 0)
        {
            return new AlertsByTypePage([], 0);
        }

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 100 ? 20 : request.PageSize;

        var (items, totalCount) = await alerts.GetByTypesPagedAsync(
            request.UserId, request.Types, page, pageSize, cancellationToken);

        var dtos = items.Select(a => new AlertDto(
            a.Id, a.Type, a.Severity, a.Title, a.Message,
            a.ReferenceId, a.ReferenceLabel, a.IsRead, a.IsResolved,
            a.CreatedAt, a.ResolvedAt, a.OccurrenceCount, a.LastOccurredAt)).ToList();

        return new AlertsByTypePage(dtos, totalCount);
    }
}
