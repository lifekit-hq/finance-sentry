namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Exceptions;

/// <param name="Status">
/// <c>scheduled</c> or <c>released</c> to keep only that status (#1091); null or blank keeps both, anything else is
/// rejected (400, <c>MACRO_STATUS_INVALID</c>) rather than silently narrowed.
/// </param>
public record GetMacroCalendarQuery(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<string>? Regions,
    string? MinImportance,
    string? Status = null) : IQuery<IReadOnlyList<MacroEventDto>>;

public class GetMacroCalendarQueryHandler(IMacroCalendarService svc, TimeProvider clock)
    : IQueryHandler<GetMacroCalendarQuery, IReadOnlyList<MacroEventDto>>
{
    public async Task<IReadOnlyList<MacroEventDto>> Handle(GetMacroCalendarQuery query, CancellationToken ct)
    {
        var status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status.Trim().ToLowerInvariant();
        if (status is not null && !MacroEventStatus.IsKnown(status))
        {
            throw new MacroStatusInvalidException(query.Status!);
        }

        var items = await svc.QueryAsync(query.From, query.To, query.Regions, query.MinImportance, ct);
        var now = clock.GetUtcNow();
        return items
            .Select(e =>
            {
                var at = MacroEventTiming.EventAt(e);
                return new MacroEventDto(
                    e.Id, e.EventDate, e.EventTime, e.Event, e.Region, e.Importance, e.Source,
                    at, MacroEventTiming.StatusAt(at, now), MacroEventTiming.Key(e));
            })
            .Where(e => status is null || e.Status == status)
            .ToList();
    }
}
