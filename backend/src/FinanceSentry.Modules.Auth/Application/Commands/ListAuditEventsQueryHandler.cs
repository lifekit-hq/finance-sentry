using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceSentry.Modules.Auth.Application.Commands;

/// <summary>
/// Lists the audit events that concern the asking person. The Owner query filter scopes the read to them, and the
/// explicit <c>SubjectUserId</c> predicate says so in the query too: no path here reaches another person's events.
/// </summary>
public class ListAuditEventsQueryHandler(AuthDbContext db) : IQueryHandler<ListAuditEventsQuery, AuditEventPageDto>
{
    public async Task<AuditEventPageDto> Handle(ListAuditEventsQuery query, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(query.Take, 1, ListAuditEventsQuery.MaxTake);

        var events = db.AuditEvents.AsNoTracking().Where(e => e.SubjectUserId == query.UserId);
        if (query.Before is { } before)
        {
            var beforeId = query.BeforeId ?? Guid.Empty;
            events = events.Where(e => e.OccurredAt < before || (e.OccurredAt == before && e.Id.CompareTo(beforeId) < 0));
        }

        // One extra row says whether there is another page.
        var rows = await events
            .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
            .Take(take + 1)
            .ToListAsync(cancellationToken);

        var page = rows.Take(take)
            .Select(e => new AuditEventDto(
                e.Id, e.OccurredAt, e.ActorKind.ToString(), e.Action.ToString(), e.ResourceType, e.ResourceId, e.FieldNames, e.TraceId))
            .ToList();

        var hasMore = rows.Count > take;
        return new AuditEventPageDto(page, hasMore ? page[^1].OccurredAt : null, hasMore ? page[^1].Id : null);
    }
}
