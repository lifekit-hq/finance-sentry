namespace FinanceSentry.Modules.Wealth.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Wealth.Application.Services;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

/// <summary>
/// One entry of the career timeline. <paramref name="Label"/>, <paramref name="Amount"/> and <paramref name="Note"/> are null
/// when the host cannot read the encrypted fields; <paramref name="Counted"/> is whether the scenario applies the event
/// (not retired, and dated after today).
/// </summary>
public record CareerTimelineEntry(
    Guid Id,
    CareerEventKind Kind,
    DateOnly Date,
    DateOnly? EndDate,
    string? Label,
    decimal? Amount,
    string Currency,
    string? Note,
    bool Counted,
    bool Retired);

/// <summary>
/// The dated career-money events the person entered (#454 S5), earliest first. Amounts are in each event's own currency: an
/// income step is a signed monthly change, a leave a monthly reduction, a one-off the amount itself. They shape a scenario the
/// projections show beside the baseline; they are not a forecast.
/// </summary>
public record CareerTimelineResponse(
    bool Readable,
    IReadOnlyList<CareerTimelineEntry> Events,
    int CountedEvents,
    string Label = ScenarioText.Label,
    string Basis = ScenarioText.Basis);

public record GetCareerTimelineQuery(Guid UserId) : IQuery<CareerTimelineResponse>;

/// <summary>
/// On a host that holds no encryption key (the MCP host) it reads only the plain columns and returns the entries without a
/// label, an amount or a note - the smallest read that needs no key.
/// </summary>
public class GetCareerTimelineQueryHandler(ICareerEventRepository events, TimeProvider clock)
    : IQueryHandler<GetCareerTimelineQuery, CareerTimelineResponse>
{
    public async Task<CareerTimelineResponse> Handle(GetCareerTimelineQuery query, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        if (!events.CanReadEncryptedFields)
        {
            var entries = (await events.ListOutlinesAsync(query.UserId, cancellationToken))
                .Select(o => new CareerTimelineEntry(
                    o.Id, o.Kind, o.Date, o.EndDate, null, null, o.Currency, null, o.IsCounted(today), o.RetiredAt is not null))
                .ToList();
            return new CareerTimelineResponse(false, entries, entries.Count(e => e.Counted));
        }

        var readable = (await events.ListAsync(query.UserId, cancellationToken))
            .Select(c => new CareerTimelineEntry(
                c.Id, c.Kind, c.Date, c.EndDate, c.Label, c.Amount, c.Currency, c.Note, c.IsCounted(today), c.RetiredAt is not null))
            .ToList();
        return new CareerTimelineResponse(true, readable, readable.Count(e => e.Counted));
    }
}
