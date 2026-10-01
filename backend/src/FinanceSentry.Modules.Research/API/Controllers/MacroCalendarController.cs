namespace FinanceSentry.Modules.Research.API.Controllers;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("research/macro-calendar")]
public class MacroCalendarController(
    IQueryHandler<GetMacroCalendarQuery, IReadOnlyList<MacroEventDto>> handler) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? regions,
        [FromQuery] string? minImportance,
        CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var fromEff = from ?? today;
        var toEff = to ?? fromEff.AddDays(30);

        var regionList = string.IsNullOrWhiteSpace(regions)
            ? null
            : regions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var result = await handler.Handle(new GetMacroCalendarQuery(fromEff, toEff, regionList, minImportance), ct);
        return Ok(result);
    }
}
