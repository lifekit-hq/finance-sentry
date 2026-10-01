namespace FinanceSentry.Modules.Research.API.Controllers;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("research/news")]
public class NewsController(
    IQueryHandler<SearchMarketNewsQuery, SearchMarketNewsResult> search,
    IQueryHandler<GetNewsForTickerQuery, IReadOnlyList<NewsArticleDto>> forTicker) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] string? tickers,
        [FromQuery] Guid? thesisId,
        [FromQuery] DateTimeOffset? since,
        [FromQuery] int limit = 25,
        CancellationToken ct = default)
    {
        var tickerList = string.IsNullOrWhiteSpace(tickers)
            ? null
            : tickers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var result = await search.Handle(new SearchMarketNewsQuery(q, tickerList, thesisId, since, limit), ct);
        return Ok(result.Articles);
    }

    [HttpGet("{ticker}")]
    public async Task<IActionResult> ForTicker(
        string ticker,
        [FromQuery] DateTimeOffset? since,
        [FromQuery] int limit = 25,
        CancellationToken ct = default)
    {
        var result = await forTicker.Handle(new GetNewsForTickerQuery(ticker, since, limit), ct);
        return Ok(result);
    }
}
