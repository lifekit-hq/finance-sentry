namespace FinanceSentry.Modules.Research.API.Controllers;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// A single symbol's scorecard with its verdict word and full drill-down (spec 726 US2, FR-021).
/// </summary>
[ApiController]
[Authorize]
[Route("research/scorecards")]
public class ScorecardsController(IQueryHandler<GetScorecardQuery, ScorecardResult> handler) : ControllerBase
{
    [HttpGet("{symbol}")]
    public async Task<IActionResult> Get(string symbol, CancellationToken ct) =>
        Ok(await handler.Handle(new GetScorecardQuery(symbol), ct));
}
