namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Domain.Repositories;

public record GetNewsForTickerQuery(
    string Ticker,
    DateTimeOffset? Since,
    int Limit,
    DateTimeOffset? Until = null) : IQuery<IReadOnlyList<NewsArticleDto>>;

public class GetNewsForTickerQueryHandler(INewsRepository repo)
    : IQueryHandler<GetNewsForTickerQuery, IReadOnlyList<NewsArticleDto>>
{
    public async Task<IReadOnlyList<NewsArticleDto>> Handle(GetNewsForTickerQuery query, CancellationToken ct)
    {
        var limit = query.Limit <= 0 ? 25 : query.Limit;
        var items = query is { Since: { } from, Until: { } until }
            ? await repo.GetForTickerBetweenAsync(query.Ticker, from, until, limit, ct)
            : await repo.GetForTickerAsync(query.Ticker, query.Since, limit, ct);

        return items
            .Select(a => new NewsArticleDto(
                a.Id, a.Source, a.Title, a.Url, a.Summary,
                a.Tickers, a.Categories, a.PublishedAt))
            .ToList();
    }
}
