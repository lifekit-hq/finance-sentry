namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Repositories;

public record GetWatchlistQuery(Guid UserId) : IQuery<IReadOnlyList<WatchlistItemDto>>;

public class GetWatchlistQueryHandler(IWatchlistRepository repo, ThesisOriginReader originReader)
    : IQueryHandler<GetWatchlistQuery, IReadOnlyList<WatchlistItemDto>>
{
    public async Task<IReadOnlyList<WatchlistItemDto>> Handle(GetWatchlistQuery query, CancellationToken ct)
    {
        var items = await repo.ListAsync(query.UserId, ct);
        var origins = await originReader.ForWatchlistAsync(query.UserId, items.Select(w => w.Id).ToList(), ct);
        return items
            .Select(w => new WatchlistItemDto(
                w.Id, w.Ticker, w.Exchange, w.Note, w.AddedAt, ThesisOriginReader.Resolve(origins, w.Id).ToString()))
            .ToList();
    }
}
