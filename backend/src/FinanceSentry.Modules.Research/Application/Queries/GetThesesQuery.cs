namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Repositories;

public record GetThesesQuery(Guid UserId) : IQuery<IReadOnlyList<ThesisDto>>;

public class GetThesesQueryHandler(IThesisRepository repo, ThesisOriginReader originReader)
    : IQueryHandler<GetThesesQuery, IReadOnlyList<ThesisDto>>
{
    public async Task<IReadOnlyList<ThesisDto>> Handle(GetThesesQuery query, CancellationToken ct)
    {
        var items = await repo.ListAsync(query.UserId, ct);
        var origins = await originReader.ForThesesAsync(query.UserId, items.Select(t => t.Id).ToList(), ct);
        return items
            .Select(t => new ThesisDto(
                t.Id, t.Ticker, t.ThesisText,
                t.KeyDataPoints, t.Catalysts, t.InvalidationTriggers,
                t.CreatedAt, t.UpdatedAt, t.BrokenAt, t.BrokenReason, t.EntryPrice,
                ThesisOriginReader.Resolve(origins, t.Id).ToString()))
            .ToList();
    }
}
