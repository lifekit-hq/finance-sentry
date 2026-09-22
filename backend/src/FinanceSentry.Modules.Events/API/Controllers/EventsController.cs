namespace FinanceSentry.Modules.Events.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Events.API.Responses;
using FinanceSentry.Modules.Events.Application.Queries;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("events")]
public class EventsController(
    IQueryHandler<GetUpcomingEventsQuery, UpcomingEventsResult> upcoming,
    IQueryHandler<GetFiredEventsQuery, FiredEventsPageResponse> fired) : ControllerBase
{
    /// <summary>Upcoming events in a window (default today .. +90 days). <c>kinds</c> is comma-separated.</summary>
    [HttpGet("upcoming")]
    public async Task<ActionResult<UpcomingEventsResult>> GetUpcoming(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? kinds,
        CancellationToken ct)
    {
        var result = await upcoming.Handle(
            new GetUpcomingEventsQuery(User.RequireUserId(), from, to, SplitKinds(kinds)), ct);
        return Ok(result);
    }

    /// <summary>Fired events (the five detector alert types), newest first, paged.</summary>
    [HttpGet("fired")]
    public async Task<ActionResult<FiredEventsPageResponse>> GetFired(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = GetFiredEventsQueryHandler.DefaultPageSize,
        [FromQuery] string? kinds = null,
        CancellationToken ct = default)
    {
        var result = await fired.Handle(
            new GetFiredEventsQuery(User.RequireUserId(), page, pageSize, SplitKinds(kinds)), ct);
        return Ok(result);
    }

    private static IReadOnlyCollection<string>? SplitKinds(string? kinds)
        => string.IsNullOrWhiteSpace(kinds)
            ? null
            : kinds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
