namespace FinanceSentry.Modules.Research.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("research/watchlist")]
public class WatchlistController(
    IQueryHandler<GetWatchlistQuery, IReadOnlyList<WatchlistItemDto>> getWatchlist,
    ICommandHandler<AddWatchlistItemCommand, WatchlistItemDto> addItem,
    ICommandHandler<RemoveWatchlistItemCommand, bool> removeItem) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var items = await getWatchlist.Handle(new GetWatchlistQuery(User.RequireUserId()), ct);
        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] AddWatchlistItemRequest body, CancellationToken ct)
    {
        var created = await addItem.Handle(
            new AddWatchlistItemCommand(User.RequireUserId(), body.Ticker, body.Exchange, body.Note), ct);
        return CreatedAtAction(nameof(List), new { id = created.Id }, created);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    {
        var ok = await removeItem.Handle(new RemoveWatchlistItemCommand(User.RequireUserId(), id), ct);
        if (!ok)
        {
            throw new WatchlistItemNotFoundException();
        }

        return NoContent();
    }
}

public record AddWatchlistItemRequest(string Ticker, string? Exchange, string? Note);
