namespace FinanceSentry.Modules.Wealth.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Wealth.Application.Commands;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

public record GetManualHoldingsQuery(Guid UserId) : IQuery<IReadOnlyList<ManualHoldingDto>>;

public class GetManualHoldingsQueryHandler(IManualHoldingRepository holdings)
    : IQueryHandler<GetManualHoldingsQuery, IReadOnlyList<ManualHoldingDto>>
{
    public async Task<IReadOnlyList<ManualHoldingDto>> Handle(GetManualHoldingsQuery query, CancellationToken cancellationToken)
        => [.. (await holdings.ListAsync(query.UserId, cancellationToken)).Select(ManualHoldingDto.From)];
}
