namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Domain.Repositories;

public record ListThesisEventsQuery(Guid UserId, Guid? SubjectId) : IQuery<IReadOnlyList<ThesisEventDto>>;

/// <summary>Read-only projection of a user's price-stamped lifecycle event trail (FR-001, US1).</summary>
public class ListThesisEventsQueryHandler(IThesisEventRepository repo)
    : IQueryHandler<ListThesisEventsQuery, IReadOnlyList<ThesisEventDto>>
{
    public async Task<IReadOnlyList<ThesisEventDto>> Handle(
        ListThesisEventsQuery query, CancellationToken ct)
    {
        var events = await repo.ListAsync(query.UserId, query.SubjectId, ct);

        return events
            .Select(e => new ThesisEventDto(
                e.Id,
                e.SubjectType,
                e.SubjectId,
                e.Ticker,
                e.EventType,
                e.Timestamp,
                e.SubjectPrice,
                e.BenchmarkPrice,
                e.BenchmarkTicker,
                e.PricesPending,
                e.DecisionNote,
                e.Origin.ToString(),
                e.Audit))
            .ToList();
    }
}
