namespace FinanceSentry.Modules.Wealth.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Wealth.Application.Commands;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

/// <summary>The person's declared income, newest entry last. Reads the encrypted fields, so it runs on the API host only.</summary>
public record GetIncomeSourcesQuery(Guid UserId) : IQuery<IReadOnlyList<IncomeSourceDto>>;

public class GetIncomeSourcesQueryHandler(IIncomeSourceRepository sources, TimeProvider clock)
    : IQueryHandler<GetIncomeSourcesQuery, IReadOnlyList<IncomeSourceDto>>
{
    public async Task<IReadOnlyList<IncomeSourceDto>> Handle(GetIncomeSourcesQuery query, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return [.. (await sources.ListAsync(query.UserId, cancellationToken)).Select(s => IncomeSourceDto.From(s, today))];
    }
}
