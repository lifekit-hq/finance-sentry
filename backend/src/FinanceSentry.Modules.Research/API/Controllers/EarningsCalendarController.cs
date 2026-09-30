namespace FinanceSentry.Modules.Research.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("research/earnings-calendar")]
public class EarningsCalendarController(
    IQueryHandler<GetEarningsCalendarQuery, IReadOnlyList<EarningsEventDto>> handler) : ControllerBase
{
    private const int DefaultWindowDays = 90;

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? tickers,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? eventType,
        CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var fromEff = from ?? today;
        var toEff = to ?? fromEff.AddDays(DefaultWindowDays);

        var tickerList = string.IsNullOrWhiteSpace(tickers)
            ? null
            : tickers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var result = await handler.Handle(
            new GetEarningsCalendarQuery(tickerList, User.RequireUserId(), fromEff, toEff, eventType), ct);
        return Ok(result);
    }
}
