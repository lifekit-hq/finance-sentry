namespace FinanceSentry.Modules.Research.API.Controllers;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("research")]
public class FilingsController(
    IQueryHandler<GetRecentFilingsQuery, IReadOnlyList<EdgarFilingDto>> filingsHandler,
    IQueryHandler<GetFundamentalsQuery, IReadOnlyList<FundamentalFactDto>> fundamentalsHandler) : ControllerBase
{
    private const int DefaultFilingLimit = 10;
    private const int DefaultMaxPerConcept = 5;

    [HttpGet("filings/{ticker}")]
    public async Task<IActionResult> Filings(
        string ticker,
        [FromQuery] string? formTypes,
        [FromQuery] int? limit,
        CancellationToken ct = default)
    {
        var forms = string.IsNullOrWhiteSpace(formTypes)
            ? null
            : formTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var result = await filingsHandler.Handle(
            new GetRecentFilingsQuery(ticker, forms, limit ?? DefaultFilingLimit), ct);
        return Ok(result);
    }

    [HttpGet("fundamentals/{ticker}")]
    public async Task<IActionResult> Fundamentals(
        string ticker,
        [FromQuery] int? maxPerConcept,
        CancellationToken ct = default)
    {
        var result = await fundamentalsHandler.Handle(
            new GetFundamentalsQuery(ticker, maxPerConcept ?? DefaultMaxPerConcept), ct);
        return Ok(result);
    }
}
