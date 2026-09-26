namespace FinanceSentry.Modules.Events.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Events.API.Responses;
using FinanceSentry.Modules.Events.Domain;
using FinanceSentry.Modules.Events.Domain.Ports;
using FinanceSentry.Modules.Events.Domain.Repositories;

/// <summary>
/// Answers, for one UTC day: what fired, what was judged, what was sent, and what was deliberately
/// withheld (feature 687, issue #687). Unlike <see cref="GetFiredEventsQuery"/> - which is alert-table
/// centric and only ever sees five detector alert types - this reads the companion outbox directly, so
/// every event kind is in view, including one with no alert behind it (e.g. AnalystAction). A day with
/// nothing fired is honest emptiness: zero items, zero counts, never fabricated.
/// </summary>
public sealed record GetDailyEventOutcomesQuery(Guid UserId, DateOnly Date) : IQuery<DailyEventOutcomesResponse>;

public sealed class GetDailyEventOutcomesQueryHandler(
    IEventDeliveryReader delivery,
    IEventVerdictRepository verdicts)
    : IQueryHandler<GetDailyEventOutcomesQuery, DailyEventOutcomesResponse>
{
    public async Task<DailyEventOutcomesResponse> Handle(GetDailyEventOutcomesQuery query, CancellationToken ct)
    {
        var fired = await delivery.ListForDateAsync(query.UserId, query.Date, ct);
        var eventIds = fired.Select(f => f.EventId).ToList();

        var verdictByEvent = eventIds.Count == 0
            ? new Dictionary<Guid, EventVerdict>()
            : (await verdicts.ListByCompanionEventIdsAsync(query.UserId, eventIds, ct))
                .ToDictionary(v => v.CompanionEventId);

        var items = fired.Select(f =>
        {
            verdictByEvent.TryGetValue(f.EventId, out var v);
            var outcome = EventOutcome.From(f.Disposition, v);
            return new DailyEventOutcomeDto(
                f.EventId,
                f.AlertId,
                f.Kind,
                f.Subject,
                f.OccurredAt,
                f.Disposition,
                v is null ? null : new EventVerdictDto(v.Verdict, v.Notified, v.RecordedAt),
                outcome);
        }).ToList();

        var sent = items.Count(i => i.Outcome == EventOutcome.Verdict);
        var withheld = items.Count(i => i.Outcome == EventOutcome.JudgedImmaterial);

        return new DailyEventOutcomesResponse(
            query.Date, items, items.Count, sent + withheld, sent, withheld, DateTimeOffset.UtcNow);
    }
}
