namespace FinanceSentry.Modules.Research.API.Controllers;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("research/quotes")]
public class QuotesController(
    IQueryHandler<GetQuotesQuery, IReadOnlyList<QuoteDto>> getQuotes) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string tickers, CancellationToken ct)
    {
        var list = (tickers ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var quotes = await getQuotes.Handle(new GetQuotesQuery(list), ct);
        return Ok(quotes);
    }
}
