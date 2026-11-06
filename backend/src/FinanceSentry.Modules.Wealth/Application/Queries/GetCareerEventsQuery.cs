namespace FinanceSentry.Modules.Wealth.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Wealth.Application.Commands;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

/// <summary>The person's career timeline, earliest first. Reads the encrypted fields, so it runs on the API host only.</summary>
public record GetCareerEventsQuery(Guid UserId) : IQuery<IReadOnlyList<CareerEventDto>>;

public class GetCareerEventsQueryHandler(ICareerEventRepository events, TimeProvider clock)
    : IQueryHandler<GetCareerEventsQuery, IReadOnlyList<CareerEventDto>>
{
    public async Task<IReadOnlyList<CareerEventDto>> Handle(GetCareerEventsQuery query, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return [.. (await events.ListAsync(query.UserId, cancellationToken)).Select(c => CareerEventDto.From(c, today))];
    }
}
