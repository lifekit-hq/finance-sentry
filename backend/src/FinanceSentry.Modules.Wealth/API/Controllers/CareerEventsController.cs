namespace FinanceSentry.Modules.Wealth.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Wealth.Application.Commands;
using FinanceSentry.Modules.Wealth.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// The career-money timeline the person enters (#454 S5). Writes are audited by field name (the context's interceptor); the
/// person reading their own list is not audited, a read through MCP or the in-app agent is (<c>get_career_timeline</c>).
/// </summary>
[ApiController]
[Authorize]
[Route("wealth/career-events")]
public class CareerEventsController(
    IQueryHandler<GetCareerEventsQuery, IReadOnlyList<CareerEventDto>> list,
    ICommandHandler<CreateCareerEventCommand, CareerEventDto> create,
    ICommandHandler<UpdateCareerEventCommand, CareerEventDto> update,
    ICommandHandler<RetireCareerEventCommand, CareerEventDto> retire,
    ICommandHandler<DeleteCareerEventCommand, Unit> delete) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await list.Handle(new GetCareerEventsQuery(User.RequireUserId()), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CareerEventInput request, CancellationToken ct)
    {
        var result = await create.Handle(new CreateCareerEventCommand(User.RequireUserId(), request), ct);
        return Created($"/api/v1/wealth/career-events/{result.Id}", result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CareerEventInput request, CancellationToken ct)
        => Ok(await update.Handle(new UpdateCareerEventCommand(User.RequireUserId(), id, request), ct));

    [HttpPost("{id:guid}/retire")]
    public async Task<IActionResult> Retire(Guid id, CancellationToken ct)
        => Ok(await retire.Handle(new RetireCareerEventCommand(User.RequireUserId(), id), ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await delete.Handle(new DeleteCareerEventCommand(User.RequireUserId(), id), ct);
        return NoContent();
    }
}
