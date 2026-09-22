namespace FinanceSentry.Modules.Events.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Events.API.Responses;
using FinanceSentry.Modules.Events.Domain;
using FinanceSentry.Modules.Events.Domain.Ports;
using FinanceSentry.Modules.Events.Domain.Repositories;

/// <summary>Fired events for a user, newest first, paged. <paramref name="Kinds"/> narrows within the five event types.</summary>
public sealed record GetFiredEventsQuery(
    Guid UserId,
    int Page,
    int PageSize,
    IReadOnlyCollection<string>? Kinds) : IQuery<FiredEventsPageResponse>;

/// <summary>
/// Alert-centric feed enriched left to right: the alerts that fired, the outbox row that carried
/// each to the reader, the verdict the reader recorded. Verdicts are looked up by alert id, so one
/// still renders after its companion row has purged. The outcome is derived per row by
/// <see cref="EventOutcome.From"/>; an absent reader leaves every row awaiting or not delivered and
/// nothing here fails.
/// </summary>
public sealed class GetFiredEventsQueryHandler(
    IFiredAlertReader alerts,
    IEventDeliveryReader delivery,
    IEventVerdictRepository verdicts)
    : IQueryHandler<GetFiredEventsQuery, FiredEventsPageResponse>
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public async Task<FiredEventsPageResponse> Handle(GetFiredEventsQuery query, CancellationToken ct)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > MaxPageSize ? DefaultPageSize : query.PageSize;
        var types = ResolveTypes(query.Kinds);

        if (types.Count == 0)
        {
            return new FiredEventsPageResponse([], 0, page, pageSize, 0);
        }

        var fired = await alerts.ListAsync(query.UserId, types, page, pageSize, ct);
        var alertIds = fired.Items.Select(a => a.AlertId).ToList();

        var deliveries = alertIds.Count == 0
            ? []
            : await delivery.ListForAlertsAsync(query.UserId, alertIds, ct);
        var deliveryByAlert = deliveries
            .Where(d => d.AlertId is not null)
            .GroupBy(d => d.AlertId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.OccurredAt).First());

        var verdictByAlert = alertIds.Count == 0
            ? new Dictionary<Guid, EventVerdict>()
            : (await verdicts.ListByAlertIdsAsync(query.UserId, alertIds, ct)).ToDictionary(v => v.AlertId);

        var items = fired.Items.Select(a =>
        {
            deliveryByAlert.TryGetValue(a.AlertId, out var d);
            verdictByAlert.TryGetValue(a.AlertId, out var v);

            return new FiredEventDto(
                a.AlertId,
                a.Type,
                a.Severity,
                a.ReferenceLabel ?? a.Title,
                a.Title,
                a.Message,
                a.CreatedAt,
                a.IsRead,
                d is null ? null : new EventDeliveryDto(d.EventId, d.Disposition, d.DispatchedAt, d.DeliveredAt),
                v is null ? null : new EventVerdictDto(v.Verdict, v.Notified, v.RecordedAt),
                EventOutcome.From(d?.Disposition, v));
        }).ToList();

        var totalPages = (int)Math.Ceiling((double)fired.TotalCount / pageSize);
        return new FiredEventsPageResponse(items, fired.TotalCount, page, pageSize, totalPages);
    }

    private static IReadOnlyCollection<string> ResolveTypes(IReadOnlyCollection<string>? kinds)
    {
        if (kinds is null || kinds.Count == 0)
        {
            return FiredEventTypes.All;
        }

        var wanted = kinds.Select(k => k.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return FiredEventTypes.All.Where(wanted.Contains).ToList();
    }
}
